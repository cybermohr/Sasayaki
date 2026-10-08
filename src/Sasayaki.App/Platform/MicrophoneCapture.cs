using NAudio.CoreAudioApi;
using NAudio.Wave;
using Sasayaki.Core;

namespace Sasayaki.App.Platform;

public sealed record MicrophoneOption(string Id, string Name);

public sealed class MicrophoneCapture(string deviceId, Action<byte[]> output) : IAsyncDisposable
{
    private MicrophoneSegment? segment;
    public event Action<Exception>? Failed;
    public event Action<float>? Level;
    public static IReadOnlyList<MicrophoneOption> Devices() => MicrophoneSegment.Devices();
    public void Start()
    {
        if (segment != null) return;
        segment = new MicrophoneSegment(deviceId, output);
        segment.Failed += e => Failed?.Invoke(e);
        segment.Level += level => Level?.Invoke(level);
        segment.Start();
    }
    public async Task StopAsync()
    {
        var current = segment;
        segment = null;
        if (current == null) return;
        try { await current.StopAsync(); }
        finally { await current.DisposeAsync(); }
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}

internal sealed class MicrophoneSegment : IAsyncDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly MMDevice device;
    private readonly WasapiCapture capture;
    private readonly CaptureBuffer buffered;
    private readonly MediaFoundationResampler resampler;
    private readonly PcmFramer framer;
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly byte[] converted = new byte[3200];
    private bool stopping, started;
    private Task? conversion;
    public event Action<Exception>? Failed;
    public event Action<float>? Level;

    public static IReadOnlyList<MicrophoneOption> Devices()
    {
        using var devices = new MMDeviceEnumerator();
        var result = new List<MicrophoneOption> { new("", "Windows default microphone") };
        foreach (var item in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        { using (item) result.Add(new(item.ID, item.FriendlyName)); }
        return result;
    }
    public MicrophoneSegment(string deviceId, Action<byte[]> output)
    {
        device = string.IsNullOrWhiteSpace(deviceId) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications) : enumerator.GetDevice(deviceId);
        capture = new WasapiCapture(device);
        buffered = new CaptureBuffer(capture.WaveFormat);
        resampler = new MediaFoundationResampler(buffered, new WaveFormat(24000, 16, 1)) { ResamplerQuality = 60 };
        framer = new(output);
        capture.DataAvailable += OnData;
        capture.RecordingStopped += (_, e) =>
        {
            try
            {
                buffered.Complete();
                if (e.Exception != null) { stopped.TrySetException(e.Exception); Failed?.Invoke(e.Exception); }
                else stopped.TrySetResult();
            }
            catch (Exception error) { stopped.TrySetException(error); Failed?.Invoke(error); }
        };
    }
    public void Start()
    {
        conversion = Task.Run(() =>
        {
            try { Drain(); framer.Flush(); }
            catch (Exception error) { buffered.Complete(); Failed?.Invoke(error); throw; }
        });
        try { capture.StartRecording(); started = true; }
        catch { buffered.Complete(); throw; }
    }
    private void OnData(object? sender, WaveInEventArgs e)
    {
        try { buffered.AddSamples(e.Buffer, e.BytesRecorded); }
        catch (Exception error) { Failed?.Invoke(error); }
    }
    private void Drain()
    {
        int read;
        while ((read = resampler.Read(converted, 0, converted.Length)) > 0)
        {
            var peak = 0f;
            for (var i = 0; i + 1 < read; i += 2)
                peak = Math.Max(peak, Math.Abs((float)BitConverter.ToInt16(converted, i) / 32768));
            Level?.Invoke(peak);
            framer.Write(converted.AsSpan(0, read));
        }
    }
    public async Task StopAsync()
    {
        if (started)
        {
            if (!stopping) { stopping = true; capture.StopRecording(); }
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        else buffered.Complete();
        if (conversion != null) await conversion.WaitAsync(TimeSpan.FromSeconds(3));
    }
    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); } catch { /* Coordinator retains/report errors, disposal must finish. */ }
        capture.Dispose(); resampler.Dispose(); device.Dispose(); enumerator.Dispose();
    }

    // A temporary gap must not look like EOF to the Media Foundation resampler.
    // One conversion worker blocks here until capture supplies data or explicitly ends.
    private sealed class CaptureBuffer(WaveFormat format) : IWaveProvider
    {
        private readonly object gate = new();
        private readonly Queue<byte[]> chunks = new();
        private int queued, offset;
        private bool complete;
        public WaveFormat WaveFormat => format;
        public void AddSamples(byte[] data, int count)
        {
            if (count == 0) return;
            lock (gate)
            {
                if (complete) return;
                if (queued + count > format.AverageBytesPerSecond * 5)
                    throw new ServiceException("Microphone conversion fell behind. Recording stopped without dropping audio silently.");
                chunks.Enqueue(data.AsSpan(0, count).ToArray()); queued += count; Monitor.PulseAll(gate);
            }
        }
        public void Complete() { lock (gate) { complete = true; Monitor.PulseAll(gate); } }
        public int Read(byte[] buffer, int destination, int count)
        {
            lock (gate)
            {
                while (chunks.Count == 0 && !complete) Monitor.Wait(gate);
                if (chunks.Count == 0) return 0;
                var head = chunks.Peek(); var read = Math.Min(count, head.Length - offset);
                Array.Copy(head, offset, buffer, destination, read); offset += read; queued -= read;
                if (offset == head.Length) { chunks.Dequeue(); offset = 0; }
                return read;
            }
        }
    }
}
