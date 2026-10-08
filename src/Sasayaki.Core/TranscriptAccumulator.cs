using System.Text;
using System.Text.Json;

namespace Sasayaki.Core;

public sealed class TranscriptAccumulator
{
    private readonly StringBuilder stable = new();
    private string suffix = "";
    public string Preview => stable.ToString() + suffix;
    public string Stable => stable.ToString();
    public string? Final { get; private set; }
    public void Accept(JsonElement message)
    {
        var type = message.GetProperty("type").GetString();
        switch (type)
        {
            case "conversation.item.input_audio_transcription.delta":
                stable.Append(message.GetProperty("delta").GetString()); suffix = ""; break;
            case "conversation.item.input_audio_transcription.intermediate":
                suffix = message.GetProperty("intermediate").GetString() ?? ""; break;
            case "conversation.item.input_audio_transcription.completed":
                Final = message.GetProperty("transcript").GetString() ?? ""; break;
            case "error":
            case "conversation.item.input_audio_transcription.failed":
                // Service messages may echo user input. Never expose the raw payload.
                throw new ServiceException("Speech transcription failed. Check the deployment, credentials, and connection.");
        }
    }
}
