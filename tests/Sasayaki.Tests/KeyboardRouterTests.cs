using Sasayaki.Core;
using Xunit;

namespace Sasayaki.Tests;

public sealed class KeyboardRouterTests
{
    [Theory]
    [InlineData(0xA2, 0x5B)] [InlineData(0x5B, 0xA2)]
    [InlineData(0xA3, 0x5C)] [InlineData(0x5C, 0xA3)]
    [InlineData(0xA2, 0x5C)] [InlineData(0x5B, 0xA3)]
    public void ChordBalancesDeliveredAndSuppressedModifiers(int first, int second)
    {
        var router = new KeyboardRouter();
        Assert.False(router.Route((ushort)first, true).Suppress);
        var activation = router.Route((ushort)second, true);
        Assert.True(activation.Suppress); Assert.Equal("down", activation.Gesture);
        Assert.True(router.Route((ushort)second, true).Suppress);
        Assert.Null(router.Route((ushort)second, true).Gesture);
        var release = router.Route((ushort)first, false);
        Assert.False(release.Suppress); Assert.Equal("up", release.Gesture);
        Assert.True(router.Route((ushort)second, false).Suppress);
    }
    [Fact]
    public void UnrelatedShortcutReplaysHiddenModifierBeforeItsKey()
    {
        var router = new KeyboardRouter(); router.Route(0xA2, true); router.Route(0x5B, true);
        var unrelated = router.Route(0x43, true);
        Assert.Equal("other", unrelated.Gesture); Assert.True(unrelated.Suppress);
        Assert.Equal(new[] { new KeyTransition(0x5B, true), new KeyTransition(0x43, true) }, unrelated.Replay);
        Assert.False(router.Route(0x43, false).Suppress);
        Assert.False(router.Route(0x5B, false).Suppress);
        Assert.False(router.Route(0xA2, false).Suppress);
    }
    [Fact]
    public void InjectedAndRepeatedEventsDoNotActivateGesture()
    {
        var router = new KeyboardRouter(); router.Route(0xA2, true);
        Assert.Null(router.Route(0x5B, true, injected: true).Gesture);
        Assert.Null(router.Route(0xA2, true).Gesture);
        Assert.Null(router.Route(0xA2, false).Gesture);
    }
    [Fact]
    public void OrdinaryCtrlShortcutPassesThrough()
    {
        var router = new KeyboardRouter();
        foreach (var transition in new[] { new KeyTransition(0xA2, true), new KeyTransition(0x43, true), new KeyTransition(0x43, false), new KeyTransition(0xA2, false) })
        { var route = router.Route(transition.Key, transition.Down); Assert.False(route.Suppress); Assert.Null(route.Gesture); Assert.Empty(route.Replay); }
    }
    [Fact]
    public void ExistingThirdKeyPreventsActivation()
    {
        var router = new KeyboardRouter(); router.Route(0x10, true); router.Route(0xA2, true);
        var route = router.Route(0x5B, true); Assert.False(route.Suppress); Assert.Null(route.Gesture);
    }
}
