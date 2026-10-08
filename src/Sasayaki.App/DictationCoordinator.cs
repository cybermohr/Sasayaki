using System.Net.Http;
using System.Windows.Threading;
using Sasayaki.App.Platform;
using Sasayaki.Core;

namespace Sasayaki.App;

/// <summary>All session mutations run on the WPF dispatcher. Generation checks reject late callbacks.</summary>
public sealed class DictationCoordinator(Dispatcher dispatcher, Func<AppSettings> settings) : IAsyncDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly TextInjector injector = new();
    private CancellationTokenSource? cancellation;
    private StreamingTranscriber? speech;
    private MicrophoneCapture? microphone;
    private SystemAudioMute? systemAudioMute;
    private Task pause = Task.CompletedTask;
    private int generation;
    private long startedAt;
    private long finishStarted;
    private bool slowShown;
    private bool finishing, ending;
    private TaskCompletionSource? endCompleted;
    public string RetainedText { get; private set; } = "";
    public bool Armed { get; private set; }
    public bool HasSession => cancellation != null || ending;
    public event Action<string>? Status;
    public event Action<float>? Level;
    public event Action<bool>? RecordingChanged;
    public event Action? Completed;
    public event Action? Recovery;

    public void Handle(GestureEvent gesture)
    {
        switch (gesture.Action)
        {
            case GestureAction.Begin: Begin(); break;
            case GestureAction.CommitGesture: speech?.ConfirmGesture(); break;
            case GestureAction.PauseCapture:
                if (microphone != null) pause = microphone.StopAsync();
                break;
            case GestureAction.ResumeCapture: _ = ResumeAsync(generation); break;
            case GestureAction.Latched: Status?.Invoke("Hands-free recording · double-press Ctrl+Win to finish"); break;
            case GestureAction.Finish: _ = FinishAsync(generation, gesture.Timestamp); break;
            case GestureAction.Cancel: Cancel(); break;
        }
    }
    private void Begin()
    {
        if (HasSession) return;
        var id = ++generation;
        cancellation = new(); finishing = false; startedAt = Environment.TickCount64;
        if (Armed) { Status?.Invoke("Release Ctrl+Win to insert retained text"); return; }
        if (RetainedText.Length > 0)
        {
            cancellation.Dispose(); cancellation = null;
            Status?.Invoke("Resolve or discard the retained text before recording again.");
            Completed?.Invoke(); Recovery?.Invoke(); return;
        }
        try
        {
            var config = settings(); config.Validate();
            speech = new(config);
            speech.Failed += error => PostFailure(id, error);
            microphone = new(config.MicrophoneId, speech.Append);
            microphone.Failed += error => PostFailure(id, error);
            microphone.Level += value => dispatcher.BeginInvoke(() => { if (id == generation && !finishing) Level?.Invoke(value); });
            systemAudioMute = SystemAudioMute.Acquire();
            Status?.Invoke("Recording · release Ctrl+Win to finish · Esc cancels");
            speech.Start(provisional: true); microphone.Start();
            RecordingChanged?.Invoke(true);
        }
        catch (Exception error) { _ = FailAsync(id, error); }
    }
    private async Task ResumeAsync(int id)
    {
        try { await pause; if (id == generation && !finishing) microphone?.Start(); }
        catch (Exception error) { await FailAsync(id, error); }
    }
    private void PostFailure(int id, Exception error) => dispatcher.BeginInvoke(() => _ = FailAsync(id, error));
    public void Tick()
    {
        if (HasSession && finishing && !slowShown && Environment.TickCount64 - finishStarted > 2000)
        { slowShown = true; Status?.Invoke("Still processing… the two-second target has been exceeded"); }
        if (!HasSession || finishing || Armed) return;
        var remaining = 300 - (Environment.TickCount64 - startedAt) / 1000;
        if (remaining <= 0) _ = FinishAsync(generation, Environment.TickCount64);
        else if (remaining <= 15) Status?.Invoke($"Recording · finishes in {remaining} seconds");
    }
    private async Task FinishAsync(int id, long finishAt)
    {
        if (id != generation || finishing || cancellation == null) return;
        finishing = true;
        finishStarted = finishAt; slowShown = false;
        var token = cancellation.Token;
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, 10000 - (Environment.TickCount64 - finishAt))));
        try
        {
            if (!Armed)
            {
                Status?.Invoke("Transcribing…");
                await pause;
                if (id != generation) return;
                if (microphone != null) await microphone.StopAsync();
                if (id != generation) return;
                RestoreSystemAudio();
                RecordingChanged?.Invoke(false);
                var raw = speech == null ? "" : await speech.FinishAsync(token);
                if (id != generation) return;
                RetainedText = raw;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    Status?.Invoke("Cleaning dictation…");
                    var cleaned = await new TextCleaner(http).CleanAsync(raw, settings(), token);
                    if (id != generation) return;
                    RetainedText = cleaned;
                }
            }
            token.ThrowIfCancellationRequested();
            if (id != generation) return;
            Armed = false;
            if (!string.IsNullOrWhiteSpace(RetainedText))
            {
                Status?.Invoke("Inserting…");
                await injector.InsertAsync(RetainedText, token);
                if (id != generation) return;
                Status?.Invoke($"Text submitted to Windows · {Environment.TickCount64 - finishAt} ms");
            }
            else Status?.Invoke("No speech detected");
            RetainedText = "";
            await EndAsync(id);
        }
        catch (Exception error) { await FailAsync(id, error); }
    }
    private async Task FailAsync(int id, Exception error)
    {
        if (id != generation || cancellation == null) return;
        if (RetainedText.Length == 0) RetainedText = speech?.StableText ?? "";
        var message = error switch
        {
            ConfigurationException or ServiceException => error.Message,
            OperationCanceledException => "Processing timed out. Any available text is retained; nothing will be inserted later.",
            _ => "Recording or connection failed. Check the microphone and Azure settings. Any available text is retained."
        };
        Armed = false;
        await EndAsync(id);
        Status?.Invoke(message);
        Recovery?.Invoke();
    }
    private async Task EndAsync(int id)
    {
        if (ending) { await endCompleted!.Task; return; }
        if (id != generation) return;
        ending = true;
        RecordingChanged?.Invoke(false);
        var completion = endCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ++generation;
        var mic = microphone; var transcriber = speech; var source = cancellation;
        microphone = null; speech = null; cancellation = null;
        try
        {
            source?.Cancel();
            try { await pause; } catch { }
            pause = Task.CompletedTask;
            if (mic != null) { try { await mic.DisposeAsync(); } catch { } }
            RestoreSystemAudio();
            if (transcriber != null) await transcriber.DisposeAsync();
        }
        finally
        {
            RestoreSystemAudio();
            source?.Dispose();
            ending = false;
            completion.TrySetResult();
            Completed?.Invoke();
        }
    }
    private void RestoreSystemAudio()
    {
        var muted = systemAudioMute;
        systemAudioMute = null;
        muted?.Dispose();
    }
    public void Cancel()
    {
        var wasArmed = Armed;
        Armed = false;
        var id = generation;
        _ = EndAsync(id);
        if (!wasArmed) RetainedText = "";
        Status?.Invoke(wasArmed ? "Insertion disarmed · text remains available from the tray" : "Cancelled");
    }
    public void Arm()
    {
        if (HasSession || RetainedText.Length == 0) return;
        Armed = true; Status?.Invoke("Retained text armed · focus a destination, then press and release Ctrl+Win · Esc disarms");
    }
    public void Discard() { if (!HasSession) { RetainedText = ""; Armed = false; Status?.Invoke("Retained text discarded"); } }
    public async ValueTask DisposeAsync() { await EndAsync(generation); http.Dispose(); }
}
