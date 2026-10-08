using System.Runtime.InteropServices;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Sasayaki.App.Platform;
using Sasayaki.Core;

namespace Sasayaki.App;

public sealed class OverlayWindow : Window
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetStyle(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetStyle(nint window, int index, nint style);
    private readonly TextBlock label = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(4, 0, 4, 8) };
    private readonly VoiceWaveform waveform = new() { Height = 80 };
    private bool recording;
    public OverlayWindow()
    {
        Width = 320; SizeToContent = SizeToContent.Height; ShowActivated = false; ShowInTaskbar = false;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true;
        AllowsTransparency = true; Background = Brushes.Transparent;
        var panel = new StackPanel { Margin = new Thickness(14, 8, 14, 8) };
        panel.Children.Add(waveform); panel.Children.Add(label);
        Content = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromRgb(7, 7, 16)),
            Child = panel
        };
        SizeChanged += (_, _) => PositionOverlay();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetStyle(handle, -20, GetStyle(handle, -20) | 0x08000000 | 0x80 | 0x20);
            HwndSource.FromHwnd(handle)?.AddHook(NoActivate);
        };
    }
    private static nint NoActivate(nint hwnd, int message, nint w, nint l, ref bool handled)
    { if (message == 0x21) { handled = true; return 3; } return 0; }
    public void SetStatus(string status)
    {
        label.Text = status;
        UpdateLabelVisibility();
        PositionOverlay();
        if (!IsVisible) Show();
    }
    private void PositionOverlay()
    {
        Left = Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - Width - 24);
        Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - ActualHeight - 24);
    }
    public void SetLevel(float value) => waveform.SetLevel(value);
    public void SetRecording(bool value)
    {
        recording = value;
        waveform.SetRecording(value);
        UpdateLabelVisibility();
    }
    private void UpdateLabelVisibility()
    {
        var recordingStatus = label.Text.StartsWith("Recording", StringComparison.Ordinal)
            || label.Text.StartsWith("Hands-free recording", StringComparison.Ordinal);
        label.Visibility = recording || recordingStatus ? Visibility.Collapsed : Visibility.Visible;
    }
}

public sealed class SettingsWindow : Window
{
    public SettingsWindow(AppSettings current, Action<AppSettings> save)
    {
        Title = "Sasayaki settings"; Width = 580; Height = 780; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Sasayaki;component/Assets/Sasayaki.ico"));
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Azure dictation", FontSize = 25, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Hold your shortcut to dictate, or double-press to start and stop hands-free. Edit endpoint URLs and replace API keys below. Keys are protected for your Windows account. Leave key fields blank to keep saved keys.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Recording shortcut", FontSize = 17, Margin = new Thickness(0, 16, 0, 6) });
        var modifierChoices = new[] { "Ctrl+Win (default)", "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "Ctrl+Alt+Shift" };
        var modifierValues = new uint[] { 10, 3, 6, 5, 7 };
        var modifiers = new ComboBox { ItemsSource = modifierChoices, SelectedIndex = Math.Max(0, Array.IndexOf(modifierValues, current.Hotkey.Modifiers)) };
        panel.Children.Add(modifiers);
        var keyValues = Enumerable.Range(0x41, 26).Concat(Enumerable.Range(0x30, 10)).Concat(Enumerable.Range(0x70, 11)).Select(k => (ushort)k).ToArray();
        var shortcutKey = new ComboBox
        {
            ItemsSource = keyValues.Select(k => k >= 0x70 ? $"F{k - 0x6F}" : ((char)k).ToString()).ToArray(),
            SelectedIndex = Math.Max(0, Array.IndexOf(keyValues, current.Hotkey.Key)),
            IsEnabled = !current.Hotkey.IsDefault, Margin = new Thickness(0, 6, 0, 6)
        };
        modifiers.SelectionChanged += (_, _) => shortcutKey.IsEnabled = modifiers.SelectedIndex != 0;
        panel.Children.Add(shortcutKey);
        panel.Children.Add(new TextBlock { Text = "Save checks and reserves custom global shortcuts with Windows. If unavailable, choose another. Shortcuts handled privately by other apps cannot be detected. The default Ctrl+Win modifier-only gesture cannot be checked this way. Escape always cancels.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        TextBox Field(string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 4) });
            var box = new TextBox { Text = value, Padding = new Thickness(6) }; panel.Children.Add(box); return box;
        }
        PasswordBox Secret(string label)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 4) });
            var box = new PasswordBox { Padding = new Thickness(6) }; panel.Children.Add(box); return box;
        }
        var speechEndpoint = Field("Speech resource root (https://…openai.azure.com/)", current.SpeechEndpoint);
        var speechDeployment = Field("Speech deployment", current.SpeechDeployment);
        var speechKey = Secret("Speech API key");
        var cleanupEndpoint = Field("Cleanup base URL (https://…openai.azure.com/openai/v1/)", current.CleanupEndpoint);
        var cleanupDeployment = Field("Cleanup deployment", current.CleanupDeployment);
        var cleanupKey = Secret("Cleanup API key");
        panel.Children.Add(new TextBlock { Text = "Microphone", Margin = new Thickness(0, 12, 0, 4) });
        var microphones = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Id" };
        try { microphones.ItemsSource = MicrophoneCapture.Devices(); microphones.SelectedValue = current.MicrophoneId; }
        catch { microphones.ItemsSource = new[] { new MicrophoneOption("", "Windows default microphone (currently unavailable)") }; }
        if (microphones.SelectedIndex < 0) microphones.SelectedIndex = 0;
        panel.Children.Add(microphones);
        var login = new CheckBox { Content = "Start when I sign in", IsChecked = current.StartAtLogin, Margin = new Thickness(0, 12, 0, 12) }; panel.Children.Add(login);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(status);
        AppSettings Read() => new()
        {
            SpeechEndpoint = speechEndpoint.Text.Trim(), SpeechDeployment = speechDeployment.Text.Trim(),
            SpeechKey = speechKey.Password.Length == 0 ? current.SpeechKey : speechKey.Password,
            CleanupEndpoint = cleanupEndpoint.Text.Trim(), CleanupDeployment = cleanupDeployment.Text.Trim(),
            CleanupKey = cleanupKey.Password.Length == 0 ? current.CleanupKey : cleanupKey.Password,
            MicrophoneId = microphones.SelectedValue as string ?? "", StartAtLogin = login.IsChecked == true,
            Hotkey = modifiers.SelectedIndex == 0 ? new() : new HotkeySettings { Modifiers = modifierValues[modifiers.SelectedIndex], Key = keyValues[shortcutKey.SelectedIndex] }
        };
        var test = new Button { Content = "Test Azure connections (uses cleanup tokens)", Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8) };
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false; status.Text = "Testing speech configuration and cleanup…";
            try
            {
                var config = Read(); config.Validate();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var speech = new StreamingTranscriber(config); speech.Start();
                await speech.WaitUntilReadyAsync(timeout.Token);
                using var http = new HttpClient();
                _ = await new TextCleaner(http).CleanAsync("This is a connection test.", config, timeout.Token);
                status.Text = "Speech session and cleanup request succeeded. Microphone transcription still needs a dictation test.";
            }
            catch (Exception e) { status.Text = e is ConfigurationException or ServiceException ? e.Message : "Connection test failed. Check keys, deployment names, network access, and supported model parameters."; }
            finally { test.IsEnabled = true; }
        };
        panel.Children.Add(test);
        var button = new Button { Content = "Save settings", Padding = new Thickness(10) };
        button.Click += (_, _) =>
        {
            try { var config = Read(); config.Validate(); save(config); Close(); }
            catch (Exception e) { status.Text = e is ConfigurationException ? e.Message : "Could not save protected settings. Check access to your local application data folder."; }
        };
        panel.Children.Add(button);
    }
}

public sealed class RecoveryWindow : Window
{
    public RecoveryWindow(string text, Action arm, Action discard)
    {
        var arming = false;
        Title = "Sasayaki retained text"; Width = 620; Height = 430; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new DockPanel { Margin = new Thickness(20) }; Content = panel;
        var note = new TextBlock { Text = "This may be an incomplete transcript, uncleaned text, or text already partly submitted. Review it and check the destination before retrying. Text stays in memory until discarded or you quit.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var insert = new Button { Content = "Arm retained text for insertion", Padding = new Thickness(10), IsEnabled = text.Length > 0 };
        insert.Click += (_, _) => { arming = true; Close(); arm(); }; buttons.Children.Add(insert);
        var clear = new Button { Content = "Discard", Padding = new Thickness(10), Margin = new Thickness(10, 0, 0, 0) };
        clear.Click += (_, _) => { discard(); Close(); }; buttons.Children.Add(clear);
        Closing += (_, _) => { if (!arming) discard(); };
        // Read-only text display has no clipboard command bindings.
        panel.Children.Add(new ScrollViewer { Content = new TextBlock { Text = text.Length == 0 ? "No text was received. Check Settings and try again." : text, TextWrapping = TextWrapping.Wrap }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
}
