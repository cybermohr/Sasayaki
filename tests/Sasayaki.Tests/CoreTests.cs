using System.Text.Json;
using System.Threading.Channels;
using Sasayaki.Core;
using Xunit;

namespace Sasayaki.Tests;

public sealed class CoreTests
{
    [Fact]
    public void HoldFinalizesOnceAtRelease()
    {
        var machine = new HotkeyMachine(); var events = new List<GestureEvent>(); machine.Action += events.Add;
        machine.Down(100); machine.Tick(350); machine.Up(600); machine.Up(700); machine.Down(800);
        Assert.Equal(new[] { GestureAction.Begin, GestureAction.CommitGesture, GestureAction.Finish }, events.Select(e => e.Action));
        Assert.Equal(600, events[^1].Timestamp); Assert.Equal(GestureState.Busy, machine.State);
    }
    [Fact]
    public void ShortTapLatencyStartsAtPhysicalRelease()
    {
        var machine = new HotkeyMachine(); var events = new List<GestureEvent>(); machine.Action += events.Add;
        machine.Down(0); machine.Up(100); machine.Tick(450);
        Assert.Equal(GestureState.TapPending, machine.State);
        machine.Tick(451);
        Assert.Equal(GestureAction.Finish, events[^1].Action); Assert.Equal(100, events[^1].Timestamp);
    }
    [Fact]
    public void DoublePressAtBoundaryLatchesAndNeedsTwoPressesToStop()
    {
        var machine = new HotkeyMachine(); var events = new List<GestureEvent>(); machine.Action += events.Add;
        machine.Down(0); machine.Up(80); machine.Down(430); machine.Up(480);
        Assert.Equal(GestureState.Latched, machine.State);
        machine.Down(1000); machine.Up(1060); machine.Tick(1411);
        Assert.Equal(GestureState.Latched, machine.State);
        machine.Down(2000); machine.Up(2060); machine.Down(2100); machine.Up(2150);
        Assert.Single(events, e => e.Action == GestureAction.Finish);
        Assert.Single(events, e => e.Action == GestureAction.Begin);
        Assert.Single(events, e => e.Action == GestureAction.ResumeCapture);
    }
    [Fact]
    public void UnrelatedShortcutCancelsProvisionalAudioBeforeCommit()
    {
        var machine = new HotkeyMachine(); var events = new List<GestureEvent>(); machine.Action += events.Add;
        machine.Down(0); machine.AbortCandidate(100); machine.Up(130); machine.Tick(500);
        Assert.Equal(new[] { GestureAction.Begin, GestureAction.Cancel }, events.Select(e => e.Action));
        Assert.Equal(GestureState.Idle, machine.State);
    }
    [Theory]
    [InlineData(1)] [InlineData(249)] [InlineData(250)] [InlineData(400)]
    public void CancelPreventsLaterFinish(int time)
    {
        var machine = new HotkeyMachine(); var events = new List<GestureEvent>(); machine.Action += events.Add;
        machine.Down(0); machine.Tick(time); machine.Cancel(time); machine.Up(time + 1); machine.Tick(time + 1000);
        Assert.DoesNotContain(events, e => e.Action == GestureAction.Finish);
    }
    [Fact]
    public void FramesPreserveOrderAndFinalShortFrame()
    {
        var output = new List<byte[]>(); var framer = new PcmFramer(output.Add);
        var source = Enumerable.Range(0, 1800).Select(n => (byte)n).ToArray();
        framer.Write(source.AsSpan(0, 333)); framer.Write(source.AsSpan(333, 800)); framer.Write(source.AsSpan(1133)); framer.Flush(); framer.Flush();
        Assert.Equal(new[] { 640, 640, 520 }, output.Select(x => x.Length)); Assert.Equal(source, output.SelectMany(x => x));
    }
    [Fact]
    public void RejectsIncompletePcmSample()
    { var framer = new PcmFramer(_ => { }); framer.Write([1]); Assert.Throws<InvalidOperationException>(framer.Flush); }
    [Fact]
    public void FinalTranscriptReplacesDeltas()
    {
        var accumulator = new TranscriptAccumulator();
        foreach (var value in new[]
        {
            "{\"type\":\"conversation.item.input_audio_transcription.delta\",\"delta\":\"Hello\"}",
            "{\"type\":\"conversation.item.input_audio_transcription.intermediate\",\"intermediate\":\" world\"}",
            "{\"type\":\"conversation.item.input_audio_transcription.intermediate\",\"intermediate\":\" there\"}",
            "{\"type\":\"conversation.item.input_audio_transcription.completed\",\"transcript\":\"Hello there.\"}"
        }) { using var json = JsonDocument.Parse(value); accumulator.Accept(json.RootElement); }
        Assert.Equal("Hello there.", accumulator.Final); Assert.Equal("Hello", accumulator.Stable);
        Assert.Equal("Hello there", accumulator.Preview);
    }
    [Theory]
    [InlineData("length", "hello", null)] [InlineData("content_filter", "hello", null)]
    [InlineData("stop", "", null)] [InlineData("stop", "hello", "refused")]
    public void CleanupRejectsUnusableResponses(string reason, string content, string? refusal)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = reason, message = new { content, refusal } } } }));
        Assert.Throws<ServiceException>(() => TextCleaner.ParseResult(json.RootElement));
    }

    [Theory]
    [InlineData("error", "unimplemented", "unimplemented")]
    [InlineData("conversation.item.input_audio_transcription.failed", "unimplemented", "unimplemented")]
    [InlineData("error", "rate_limit_exceeded", "rate limited")]
    [InlineData("error", "input_audio_buffer_commit_empty", "too little audio")]
    public void SpeechErrorsGiveActionableMessagesWithoutEchoingServicePayload(string eventType, string code, string expected)
    {
        var accumulator = new TranscriptAccumulator();
        using var partial = JsonDocument.Parse("{\"type\":\"conversation.item.input_audio_transcription.delta\",\"delta\":\"Retained text\"}");
        accumulator.Accept(partial.RootElement);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            type = eventType, error = new { type = "server_error", code, message = "private dictated text and secret-key", param = "private parameter" }
        }));
        var exception = Assert.Throws<ServiceException>(() => accumulator.Accept(json.RootElement));
        Assert.Contains(expected, exception.Message);
        Assert.DoesNotContain("private", exception.Message);
        Assert.DoesNotContain("secret-key", exception.Message);
        Assert.Equal("Retained text", accumulator.Stable);
        Assert.Null(accumulator.Final);
    }

    [Theory]
    [InlineData("{\"type\":\"error\"}")]
    [InlineData("{\"type\":\"error\",\"error\":null}")]
    [InlineData("{\"type\":\"error\",\"error\":\"private payload\"}")]
    [InlineData("{\"type\":\"error\",\"error\":{\"code\":123,\"type\":false}}")]
    [InlineData("{\"type\":\"error\",\"error\":{\"code\":\"private-key\",\"type\":\"private-text\"}}")]
    public void UnknownSpeechErrorsUseSafeFallback(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        var exception = Assert.Throws<ServiceException>(() => new TranscriptAccumulator().Accept(json.RootElement));
        Assert.Equal("Speech transcription failed. Check the deployment, credentials, and connection.", exception.Message);
    }

    [Fact]
    public void UnknownServerErrorDoesNotEchoUnknownCode()
    {
        using var json = JsonDocument.Parse("{\"type\":\"error\",\"error\":{\"type\":\"server_error\",\"code\":\"private-key\"}}");
        var exception = Assert.Throws<ServiceException>(() => new TranscriptAccumulator().Accept(json.RootElement));
        Assert.Contains("server error", exception.Message);
        Assert.DoesNotContain("private-key", exception.Message);
    }
    [Fact]
    public void CleanupAcceptsCompleteText()
    {
        using var json = JsonDocument.Parse("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"Do not deploy build 19.\"}}]}");
        Assert.Equal("Do not deploy build 19.", TextCleaner.ParseResult(json.RootElement));
    }
    internal static AppSettings Config => new()
    {
        SpeechEndpoint = "https://example.services.ai.azure.com/", SpeechKey = "synthetic-test-key",
        CleanupEndpoint = "https://example.openai.azure.com/openai/v1/", CleanupKey = "synthetic-test-key"
    };
    [Theory]
    [InlineData("http://example.services.ai.azure.com/")]
    [InlineData("https://example.services.ai.azure.com.evil.example/")]
    [InlineData("https://user:secret@example.services.ai.azure.com/")]
    [InlineData("https://example.services.ai.azure.com/?api-key=key")]
    public void RejectsUnsafeEndpoints(string endpoint) => Assert.Throws<ConfigurationException>(() => (Config with { SpeechEndpoint = endpoint }).ValidateSpeech());

    [Fact]
    public async Task StreamingWaitsForGestureThenDrainsAndCommitsExactlyOnce()
    {
        var transport = new FakeTransport();
        await using var speech = new StreamingTranscriber(Config, transport);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        speech.Start(); speech.Append(new byte[640]); speech.Append(new byte[20]);
        await speech.WaitUntilReadyAsync(deadline.Token);
        Assert.DoesNotContain(transport.Sent, s => s.Contains("input_audio_buffer.append", StringComparison.Ordinal));
        Assert.Equal("Final text.", await speech.FinishAsync(deadline.Token));
        var sent = transport.Sent.ToArray();
        Assert.Equal(2, sent.Count(s => s.Contains("input_audio_buffer.append", StringComparison.Ordinal)));
        Assert.Single(sent, s => s.Contains("input_audio_buffer.commit", StringComparison.Ordinal));
        Assert.Contains("input_audio_buffer.commit", sent[^1]);
    }
    [Fact]
    public async Task EmptyAudioDoesNotSendCommit()
    {
        var transport = new FakeTransport(); await using var speech = new StreamingTranscriber(Config, transport);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        speech.Start(); Assert.Equal("", await speech.FinishAsync(deadline.Token));
        Assert.DoesNotContain(transport.Sent, s => s.Contains("input_audio_buffer.commit", StringComparison.Ordinal));
    }
    [Fact]
    public async Task AbortedProvisionalSessionNeverUploadsAudio()
    {
        var transport = new FakeTransport(); var speech = new StreamingTranscriber(Config, transport);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        speech.Start(); speech.Append(new byte[640]); await speech.WaitUntilReadyAsync(deadline.Token);
        await speech.DisposeAsync();
        Assert.DoesNotContain(transport.Sent, s => s.Contains("input_audio_buffer.append", StringComparison.Ordinal));
    }
    [Fact]
    public async Task PendingAudioIsBounded()
    {
        await using var speech = new StreamingTranscriber(Config, new FakeTransport());
        for (var i = 0; i < 250; i++) speech.Append(new byte[640]);
        Assert.Throws<ServiceException>(() => speech.Append(new byte[640]));
    }
    private sealed class FakeTransport : IRealtimeTransport
    {
        private readonly Channel<string> events = Channel.CreateUnbounded<string>();
        public System.Collections.Concurrent.ConcurrentQueue<string> Sent { get; } = new();
        public Task ConnectAsync(Uri uri, string key, CancellationToken cancellationToken)
        { events.Writer.TryWrite("{\"type\":\"session.created\"}"); return Task.CompletedTask; }
        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            Sent.Enqueue(message);
            using var json = JsonDocument.Parse(message);
            switch (json.RootElement.GetProperty("type").GetString())
            {
                case "session.update": events.Writer.TryWrite("{\"type\":\"session.updated\"}"); break;
                case "input_audio_buffer.commit":
                    events.Writer.TryWrite("{\"type\":\"input_audio_buffer.committed\"}");
                    events.Writer.TryWrite("{\"type\":\"conversation.item.input_audio_transcription.completed\",\"transcript\":\"Final text.\"}"); break;
            }
            return Task.CompletedTask;
        }
        public async Task<string> ReceiveAsync(CancellationToken cancellationToken) => await events.Reader.ReadAsync(cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

