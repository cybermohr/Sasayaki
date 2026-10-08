# Does not read, write, or materialize clipboard data. No transcript is saved.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SasayakiProbe {
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
$form = [System.Windows.Forms.Form]::new()
$form.Text = 'Sasayaki input acceptance target'
$form.Width = 850
$form.Height = 600
$form.KeyPreview = $true
$instructions = [System.Windows.Forms.Label]::new()
$instructions.Text = 'Focus the field below and dictate using Ctrl+Win. Test both modifier orders/sides, hold, double-press, Ctrl+C, and Ctrl+Win+another key. Watch for Start menu leakage or stuck modifiers. This window never saves your text.'
$instructions.Dock = 'Top'
$instructions.Height = 65
$target = [System.Windows.Forms.TextBox]::new()
$target.Multiline = $true
$target.Dock = 'Fill'
$target.Font = [System.Drawing.Font]::new('Segoe UI', 16)
$events = [System.Windows.Forms.ListBox]::new()
$events.Dock = 'Bottom'
$events.Height = 170
$status = [System.Windows.Forms.Label]::new()
$status.Dock = 'Bottom'
$status.Height = 50
$form.Controls.Add($target)
$form.Controls.Add($events)
$form.Controls.Add($status)
$form.Controls.Add($instructions)
$initialSequence = [SasayakiProbe]::GetClipboardSequenceNumber()
$initialOwner = [SasayakiProbe]::GetClipboardOwner()
$target.Add_TextChanged({ $events.Items.Add(('Text change observed at {0:HH:mm:ss.fff}; length {1}' -f [DateTime]::Now, $target.Text.Length)) | Out-Null })
$form.Add_KeyDown({ param($sender, $event) $events.Items.Add(('DOWN {0}, modifiers {1}' -f $event.KeyCode, $event.Modifiers)) | Out-Null })
$form.Add_KeyUp({ param($sender, $event) $events.Items.Add(('UP {0}, modifiers {1}' -f $event.KeyCode, $event.Modifiers)) | Out-Null })
$timer = [System.Windows.Forms.Timer]::new()
$timer.Interval = 100
$timer.Add_Tick({
    $status.Text = 'Clipboard sequence {0} (initial {1}); owner {2} (initial {3}); target foreground {4}' -f [SasayakiProbe]::GetClipboardSequenceNumber(), $initialSequence, [SasayakiProbe]::GetClipboardOwner(), $initialOwner, ([SasayakiProbe]::GetForegroundWindow() -eq $form.Handle)
    while ($events.Items.Count -gt 100) { $events.Items.RemoveAt(0) }
})
$timer.Start()
$form.Add_Shown({ $target.Focus() | Out-Null })
try { [System.Windows.Forms.Application]::Run($form) } finally { $timer.Dispose(); $form.Dispose() }
