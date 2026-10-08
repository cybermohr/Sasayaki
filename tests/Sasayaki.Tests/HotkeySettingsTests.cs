using System.Text.Json;
using Sasayaki.Core;
using Xunit;

namespace Sasayaki.Tests;

public sealed class HotkeySettingsTests
{
    [Fact]
    public void ExistingSettingsKeepDefaultShortcut()
    {
        Assert.True(JsonSerializer.Deserialize<AppSettings>("{}")!.Hotkey.IsDefault);
        var settings = new AppSettings { Hotkey = new() { Modifiers = 3, Key = 0x44 } };
        Assert.Equal(settings.Hotkey, JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.Hotkey);
        Assert.Equal("Ctrl+Alt+D", settings.Hotkey.DisplayName);
    }

    [Theory]
    [InlineData(0, 0x44)] [InlineData(2, 0x43)] [InlineData(10, 0x44)]
    [InlineData(3, 0x7B)] [InlineData(3, 0x1B)] [InlineData(3, 0)]
    public void RejectUnsafeOrUnsupportedShortcuts(uint modifiers, ushort key)
        => Assert.Throws<ConfigurationException>(() => new HotkeySettings { Modifiers = modifiers, Key = key }.Validate());

    [Theory]
    [InlineData(0xA2, 0xA4)] [InlineData(0xA3, 0xA5)]
    public void CustomShortcutSuppressesTriggerAndBalancesRelease(ushort ctrl, ushort alt)
    {
        var router = new KeyboardRouter(new() { Modifiers = 3, Key = 0x44 });
        Assert.False(router.Route(ctrl, true).Suppress);
        Assert.False(router.Route(alt, true).Suppress);
        var down = router.Route(0x44, true);
        Assert.True(down.Suppress); Assert.Equal("down", down.Gesture);
        var repeat = router.Route(0x44, true);
        Assert.True(repeat.Suppress); Assert.Null(repeat.Gesture);
        var up = router.Route(0x44, false);
        Assert.True(up.Suppress); Assert.Equal("up", up.Gesture);
        Assert.False(router.Route(ctrl, false).Suppress);
        Assert.False(router.Route(alt, false).Suppress);
    }

    [Fact]
    public void CustomShortcutDoesNotTriggerWithExtraModifiersOrInjectedKeys()
    {
        var router = new KeyboardRouter(new() { Modifiers = 3, Key = 0x44 });
        router.Route(0xA2, true); router.Route(0xA4, true); router.Route(0xA0, true);
        Assert.Null(router.Route(0x44, true).Gesture);
        router.Reset();
        router.Route(0xA2, true); router.Route(0xA4, true);
        Assert.Null(router.Route(0x44, true, injected: true).Gesture);
        Assert.Equal("down", router.Route(0x44, true).Gesture);
        Assert.Equal("escape", router.Route(0x1B, true).Gesture);
        Assert.Equal("up", router.Route(0xA2, false).Gesture);
        Assert.True(router.Route(0x44, false).Suppress);
    }

    [Fact]
    public void CustomShortcutSupportsFunctionKeysAndBothGestureModes()
    {
        var shortcut = new HotkeySettings { Modifiers = 6, Key = 0x78 };
        shortcut.Validate(); Assert.Equal("Ctrl+Shift+F9", shortcut.DisplayName);
        var router = new KeyboardRouter(shortcut);
        var machine = new HotkeyMachine();
        var actions = new List<GestureAction>();
        machine.Action += e => actions.Add(e.Action);
        void Route(ushort key, bool down, long at)
        {
            var route = router.Route(key, down);
            if (route.Gesture == "down") machine.Down(at);
            if (route.Gesture == "up") machine.Up(at);
        }
        Route(0xA2, true, 0); Route(0xA0, true, 1);
        Route(0x78, true, 2); machine.Tick(300); Route(0x78, false, 400);
        Assert.Contains(GestureAction.Finish, actions);
        machine.Complete(); actions.Clear();
        Route(0x78, true, 500); Route(0x78, false, 550); Route(0x78, true, 600); Route(0x78, false, 650);
        Assert.Equal(GestureState.Latched, machine.State);
        Route(0x78, true, 700); Route(0x78, false, 750); Route(0x78, true, 800);
        Assert.Contains(GestureAction.Finish, actions);
    }
}
