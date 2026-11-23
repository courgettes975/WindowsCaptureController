using WindowsCaptureController.Automation;
using WindowsCaptureController.WindowsCapture;
using Controller = WindowsCaptureController.WindowsCapture.WindowsCaptureController;

namespace WindowsCaptureController.Tests;

public class OverlayMappingTests
{
    [Theory]
    [InlineData(CaptureMode.Rectangle, 0)]
    [InlineData(CaptureMode.Window, 1)]
    [InlineData(CaptureMode.Fullscreen, 2)]
    [InlineData(CaptureMode.Freeform, 3)]
    public void Mode_index_matches_measured_combo_order(CaptureMode mode, int expected)
    {
        Assert.Equal(expected, Controller.ModeIndex(mode));
    }

    [Fact]
    public void Default_new_capture_method_is_the_verified_shortcut()
    {
        // Phase 2 measurement: the ms-screenclip URI does not open the overlay for an unpackaged caller.
        Assert.Equal("shortcut", new ControllerOptions().NewCaptureMethod);
    }

    [Fact]
    public void Map_declares_four_image_modes()
    {
        Assert.Equal(4, SnippingToolUiMap.ImageModeCount);
        Assert.Equal(SnippingToolUiMap.ModeIndexFullscreen, Controller.ModeIndex(CaptureMode.Fullscreen));
    }
}
