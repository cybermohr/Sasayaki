using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Sasayaki.App.Platform;
using Sasayaki.Core;

namespace Sasayaki.App;

internal static class LiveAcceptance
{
    // Reports metadata only; a fixture is explicitly supplied by the person running the check.
    public static async Task<int> RunAsync(string fixture, string report)
    {
        object result;
        var exit = 1;
        try
        {
            var settings = new SettingsStore().Load(); settings.Validate();
            var audio = await File.ReadAllBytesAsync(fixture);
            if (audio.Length < 160000 || audio.Length > 960000 || audio.Length % 2 != 0)
                throw new ConfigurationException("Fixture must be 5–30 seconds of raw PCM16 mono 16 kHz audio.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
            await using var speech = new StreamingTranscriber(settings); speech.Start(); speech.AcceptGesture();
            await speech.WaitUntilReadyAsync(deadline.Token);
            for (var offset = 0; offset < audio.Length; offset += 640)
            {
                speech.Append(audio.AsSpan(offset, Math.Min(640, audio.Length - offset)).ToArray());
                await Task.Delay(20, deadline.Token);
            }
            var watch = Stopwatch.StartNew();
            var raw = await speech.FinishAsync(deadline.Token);
            var transcriptMs = watch.ElapsedMilliseconds;
            using var http = new HttpClient();
            var cleaned = await new TextCleaner(http).CleanAsync(raw, settings, deadline.Token);
            result = new { status = "SERVICE_CHECK_PASSED", transcriptMs, cleanupCompleteMs = watch.ElapsedMilliseconds,
                rawCharacters = raw.Length, cleanedCharacters = cleaned.Length,
                speechDeployment = settings.SpeechDeployment, cleanupDeployment = settings.CleanupDeployment,
                limitation = "No semantic fidelity, microphone capture, keyboard gesture, visible insertion, or two-second acceptance claim. Review those interactively." };
            exit = 0;
        }
        catch (Exception error)
        {
            result = new { status = error is ConfigurationException ? "BLOCKED" : "FAILED", category = error.GetType().Name,
                message = error is ConfigurationException or ServiceException ? error.Message : "Live service check failed. Check configuration and connectivity." };
            if (error is ConfigurationException) exit = 2;
        }
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return exit;
    }
}
