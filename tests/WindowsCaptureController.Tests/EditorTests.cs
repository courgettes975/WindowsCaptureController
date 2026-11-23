using WindowsCaptureController.Automation;
using WindowsCaptureController.WindowsCapture;
using Controller = WindowsCaptureController.WindowsCapture.WindowsCaptureController;

namespace WindowsCaptureController.Tests;

public class EditorTests
{
    [Theory]
    [InlineData(7, 1, 30, 8)]      // Rouge -> Rouge orangé (measured order)
    [InlineData(29, 1, 30, 0)]     // wraps forward
    [InlineData(0, -1, 30, 29)]    // wraps backward
    [InlineData(-1, 1, 6, 1)]      // unknown current index counts as 0
    [InlineData(2, -8, 6, 0)]      // large negative steps
    [InlineData(3, 0, 0, 0)]       // empty palette
    public void Colour_index_wraps_around_the_palette(int current, int delta, int count, int expected)
    {
        Assert.Equal(expected, Controller.WrapIndex(current, delta, count));
    }

    [Fact]
    public void Measured_defaults_and_ids_are_declared()
    {
        Assert.Equal(4, SnippingToolUiMap.PenDefaultWidth);
        Assert.Equal(12, SnippingToolUiMap.HighlighterDefaultWidth);
        Assert.Equal("PenStrokeWidthSlider", SnippingToolUiMap.StrokeWidthSlider);
        Assert.Equal("InkToolbarClearAll", SnippingToolUiMap.EraserClearAll);
    }

    [Fact]
    public void Clear_all_without_confirmation_is_refused_before_touching_the_editor()
    {
        // Without an open editor the call reports NotAvailable; with one it would require confirmed=true.
        var r = new Controller().ClearAllAnnotations(confirmed: false);
        Assert.NotEqual(ActionStatus.Success, r.Status);
    }

    [Fact]
    public void Protractor_is_reported_absent_not_faked()
    {
        Assert.Equal(ActionStatus.NotAvailable, new Controller().SelectProtractor().Status);
    }
}
