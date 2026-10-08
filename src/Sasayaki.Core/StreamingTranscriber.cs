using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Sasayaki.Core;

public interface IRealtimeTransport : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, string key, CancellationToken cancellationToken);
    Task SendAsync(string message, CancellationToken cancellationToken);
    Task<string> ReceiveAsync(CancellationToken cancellationToken);
}

public sealed class WebSocketTransport : IRealtimeTransport
{
    private readonly ClientWebSocket socket = new();
    public async Task ConnectAsync(Uri uri, string key, CancellationToken cancellationToken)
    {
        socket.Options.SetRequestHeader("api-key", key);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(uri, cancellationToken);
    }
    public Task SendAsync(string message, CancellationToken cancellationToken) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(message).AsMemory(), WebSocketMessageType.Text, true, cancellationToken).AsTask();
    public async Task<string> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var result = await socket.ReceiveAsync(chunk.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new ServiceException("Speech connection closed before a complete result was received.");
            if (result.MessageType != WebSocketMessageType.Text)
                throw new ServiceException("Speech returned an unexpected message format.");
            buffer.Write(chunk, 0, result.Count);
            if (buffer.Length > 1_048_576) throw new ServiceException("Speech response exceeded the allowed size.");
            if (result.EndOfMessage) return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
    }
    public ValueTask DisposeAsync() { socket.Abort(); socket.Dispose(); return ValueTask.CompletedTask; }
}

/// <summary>One recording, one ordered audio stream, one final commit. Owns no microphone.</summary>
public sealed class StreamingTranscriber : IAsyncDisposable
{
    private readonly AppSettings settings;
    private readonly IRealtimeTransport transport;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<byte[]> frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(250)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly TaskCompletionSource committedGesture = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource created = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource configured = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TranscriptAccumulator transcript = new();
    private Task? worker;
    private int finishRequested, commitSent;
    private bool disposed;
    public string StableText => transcript.Stable;
    public event Action<Exception>? Failed;
    public event Action<string>? Partial;

    public StreamingTranscriber(AppSettings settings, IRealtimeTransport? transport = null)
    { this.settings = settings; this.transport = transport ?? new WebSocketTransport(); }

    public void Start()
    {
        if (worker != null) throw new InvalidOperationException("A speech session cannot be started twice.");
        settings.ValidateSpeech();
        worker = RunAsync();
    }
    public void AcceptGesture() => committedGesture.TrySetResult();
    public void Append(byte[] frame)
    {
        if (frame.Length == 0 || frame.Length > 640 || frame.Length % 2 != 0)
            throw new ArgumentException("Audio must be an even-sized PCM16 frame of at most 640 bytes.", nameof(frame));
        if (Volatile.Read(ref finishRequested) != 0) throw new InvalidOperationException("Audio arrived after the recording was drained.");
        if (!frames.Writer.TryWrite(frame.ToArray()))
            throw new ServiceException("The speech connection fell behind. Recording stopped without dropping audio silently.");
    }
    public async Task<string> FinishAsync(CancellationToken cancellationToken)
    {
        if (worker == null) throw new InvalidOperationException("Start the speech session first.");
        Interlocked.Exchange(ref finishRequested, 1);
        AcceptGesture(); frames.Writer.TryComplete();
        return await result.Task.WaitAsync(cancellationToken);
    }
    public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => configured.Task.WaitAsync(cancellationToken);

    private async Task RunAsync()
    {
        Task? receiver = null;
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            await transport.ConnectAsync(settings.SpeechUri(), settings.SpeechKey, connectTimeout.Token);
            receiver = ReceiveAsync();
            await created.Task.WaitAsync(connectTimeout.Token);
            await transport.SendAsync(JsonSerializer.Serialize(new
            {
                type = "session.update",
                session = new { type = "transcription", audio = new { input = new
                {
                    format = new { type = "audio/pcm", rate = 16000 },
                    transcription = new { model = settings.SpeechDeployment, language = "en" },
                    turn_detection = (object?)null, noise_reduction = (object?)null
                } } }
            }), connectTimeout.Token);
            await configured.Task.WaitAsync(connectTimeout.Token);
            await committedGesture.Task.WaitAsync(lifetime.Token);
            var sent = 0;
            await foreach (var frame in frames.Reader.ReadAllAsync(lifetime.Token))
            {
                await transport.SendAsync(JsonSerializer.Serialize(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(frame) }), lifetime.Token);
                sent += frame.Length;
            }
            if (sent == 0) { result.TrySetResult(""); return; }
            Interlocked.Exchange(ref commitSent, 1);
            await transport.SendAsync("{\"type\":\"input_audio_buffer.commit\"}", lifetime.Token);
            await receiver;
        }
        catch (Exception ex)
        {
            result.TrySetException(ex); created.TrySetException(ex); configured.TrySetException(ex);
            if (!lifetime.IsCancellationRequested) Failed?.Invoke(ex);
        }
        finally
        {
            lifetime.Cancel();
            if (receiver != null) { try { await receiver; } catch { /* Exposed through result / Failed. */ } }
        }
    }

    private async Task ReceiveAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using var json = JsonDocument.Parse(await transport.ReceiveAsync(lifetime.Token));
                var message = json.RootElement;
                var type = message.GetProperty("type").GetString();
                if (type == "session.created") created.TrySetResult();
                if (type == "session.updated") configured.TrySetResult();
                transcript.Accept(message);
                if (transcript.Final is { } final)
                {
                    if (Volatile.Read(ref commitSent) == 0)
                        throw new ServiceException("Speech finalized unexpectedly before the recording was committed.");
                    result.TrySetResult(final); return;
                }
                if (type?.StartsWith("conversation.item.input_audio_transcription.", StringComparison.Ordinal) == true)
                    Partial?.Invoke(transcript.Preview);
            }
        }
        catch (Exception ex)
        {
            created.TrySetException(ex); configured.TrySetException(ex); committedGesture.TrySetException(ex);
            frames.Writer.TryComplete(ex); result.TrySetException(ex);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true; lifetime.Cancel(); frames.Writer.TryComplete();
        if (worker != null) await worker;
        // Observe faulted completion sources even if a cancelled provisional recording never awaits them.
        _ = result.Task.Exception; _ = created.Task.Exception; _ = configured.Task.Exception; _ = committedGesture.Task.Exception;
        await transport.DisposeAsync(); lifetime.Dispose();
    }
}
