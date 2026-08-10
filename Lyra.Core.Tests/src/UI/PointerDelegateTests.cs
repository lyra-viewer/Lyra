using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Layout;
using SkiaSharp;
using Xunit;

using Button = Lyra.UI.Components.Controls.Button.Button;

namespace Lyra.Core.Tests.UI;

public class PointerDelegateTests
{
    private static readonly SKPoint Origin = new(0, 0);

    // --------------------------------------------------------
    //  Dispatch reaches delegates through built-in controls
    // --------------------------------------------------------

    [Fact]
    public void HandlerFiresOnAControlThatOverridesTheHook()
    {
        var fired = 0;

        // Button overrides the pointer hooks and does not call base.
        var button = new Button("Test").OnPointerDown(_ => fired++);

        button.OnPointerDown(Origin);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void BuiltInBehaviourRunsBeforeTheHandler()
    {
        var pressedWhenHandlerRan = false;

        var checkbox = new CheckBox("Test");
        checkbox.OnPointerUp(_ => pressedWhenHandlerRan = checkbox.Checked);

        // Press then release over the control is what toggles it.
        checkbox.OnPointerEnter();
        checkbox.OnPointerDown(Origin);
        checkbox.OnPointerUp(Origin);

        // The handler observed the post-toggle state, not the pre-toggle one.
        Assert.True(checkbox.Checked);
        Assert.True(pressedWhenHandlerRan);
    }

    [Fact]
    public void HandlersCombineRatherThanReplace()
    {
        var first = 0;
        var second = 0;

        var button = new Button("Test")
            .OnPointerDown(_ => first++)
            .OnPointerDown(_ => second++);

        button.OnPointerDown(Origin);

        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void HandlerReceivesThePointerPosition()
    {
        var seen = new SKPoint(-1, -1);

        var button = new Button("Test").OnPointerMove(p => seen = p);

        button.OnPointerMove(new SKPoint(12, 34));

        Assert.Equal(new SKPoint(12, 34), seen);
    }

    // --------------------------------------------------------
    //  Enablement gating
    // --------------------------------------------------------

    [Theory]
    [InlineData("down")]
    [InlineData("up")]
    [InlineData("move")]
    [InlineData("enter")]
    public void DisabledComponentFiresNoHandler(string phase)
    {
        var fired = 0;

        var button = new Button("Test") { Enabled = false };
        button.PointerDown = _ => fired++;
        button.PointerUp = _ => fired++;
        button.PointerMove = _ => fired++;
        button.PointerEnter = () => fired++;

        Dispatch(button, phase);

        Assert.Equal(0, fired);
    }

    [Fact]
    public void DisabledComponentStillFiresPointerLeave()
    {
        var fired = 0;

        var button = new Button("Test") { Enabled = false };
        button.OnPointerLeave(() => fired++);

        button.OnPointerLeave();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void ComponentDisabledByAnAncestorFiresNoHandler()
    {
        var fired = 0;

        var parent = new VStack { Enabled = false };
        var button = new Button("Test").OnPointerDown(_ => fired++);
        parent.AddComponent(button);

        button.OnPointerDown(Origin);

        Assert.Equal(0, fired);
    }

    private static void Dispatch(ComponentBase c, string phase)
    {
        switch (phase)
        {
            case "down": c.OnPointerDown(Origin); break;
            case "up": c.OnPointerUp(Origin); break;
            case "move": c.OnPointerMove(Origin); break;
            case "enter": c.OnPointerEnter(); break;
        }
    }
}
