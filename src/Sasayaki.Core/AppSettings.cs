namespace Sasayaki.Core;

public sealed record AppSettings
{
    public string SpeechEndpoint { get; init; } = "";
    public string SpeechDeployment { get; init; } = "sasayaki-speech";
    public string SpeechKey { get; init; } = "";
    public string CleanupEndpoint { get; init; } = "";
    public string CleanupDeployment { get; init; } = "sasayaki-cleanup";
    public string CleanupKey { get; init; } = "";
    public string MicrophoneId { get; init; } = "";
    public bool StartAtLogin { get; init; }

    public void ValidateSpeech()
    {
        _ = SpeechUri();
        if (string.IsNullOrWhiteSpace(SpeechDeployment) || string.IsNullOrWhiteSpace(SpeechKey))
            throw new ConfigurationException("Enter the speech deployment name and API key in Settings.");
    }
    public void ValidateCleanup()
    {
        _ = CleanupUri();
        if (string.IsNullOrWhiteSpace(CleanupDeployment) || string.IsNullOrWhiteSpace(CleanupKey))
            throw new ConfigurationException("Enter the cleanup deployment name and API key in Settings.");
    }
    public void Validate() { ValidateSpeech(); ValidateCleanup(); }

    public Uri SpeechUri()
    {
        var uri = HttpsUri(SpeechEndpoint, "Speech");
        if (uri.AbsolutePath != "/") throw new ConfigurationException("Speech endpoint must be the resource root, without a project or API path.");
        return new UriBuilder(uri) { Scheme = "wss", Port = -1, Path = "/openai/v1/realtime", Query = "intent=transcription" }.Uri;
    }
    public Uri CleanupUri()
    {
        var uri = HttpsUri(CleanupEndpoint, "Cleanup");
        if (uri.AbsolutePath.TrimEnd('/') != "/openai/v1")
            throw new ConfigurationException("Cleanup endpoint must end in /openai/v1/.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/chat/completions");
    }
    private static Uri HttpsUri(string value, string field)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !uri.IsDefaultPort)
            throw new ConfigurationException($"{field} endpoint must be an HTTPS Azure URL without credentials, query parameters, or a custom port.");
        // Keys belong to Azure, never to an arbitrary endpoint supplied by a copied configuration.
        if (!uri.Host.EndsWith(".azure.com", StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException($"{field} endpoint must be an Azure resource hostname ending in .azure.com.");
        return uri;
    }
}

public sealed class ConfigurationException(string message) : Exception(message);
public sealed class ServiceException(string message) : Exception(message);
