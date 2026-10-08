using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Sasayaki.Core;

namespace Sasayaki.App.Platform;

public sealed class SettingsStore
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sasayaki");
    private static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        var saved = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        return saved with { SpeechKey = Unprotect(saved.SpeechKey), CleanupKey = Unprotect(saved.CleanupKey) };
    }
    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(DirectoryPath);
        var saved = settings with { SpeechKey = Protect(settings.SpeechKey), CleanupKey = Protect(settings.CleanupKey) };
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (settings.StartAtLogin)
            key.SetValue("Sasayaki", $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue("Sasayaki", false);
    }
    private static string Protect(string text) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));
    private static string Unprotect(string text) => string.IsNullOrEmpty(text) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(text), null, DataProtectionScope.CurrentUser));
}
