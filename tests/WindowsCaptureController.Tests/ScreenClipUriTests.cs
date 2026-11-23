using WindowsCaptureController.WindowsCapture;

namespace WindowsCaptureController.Tests;

public class ScreenClipUriTests
{
    [Theory]
    [InlineData(CaptureMode.Rectangle, "rectangle")]
    [InlineData(CaptureMode.Window, "window")]
    [InlineData(CaptureMode.Freeform, "freeform")]
    public void Image_uses_exactly_one_bare_mode_parameter(CaptureMode mode, string expected)
    {
        var uri = ScreenClipUri.Image(mode);

        Assert.StartsWith("ms-screenclip://capture/image?" + expected + "&", uri);
        var query = uri[(uri.IndexOf('?') + 1)..].Split('&');
        var modeParams = query.Where(q => q is "rectangle" or "window" or "freeform").ToList();
        Assert.Single(modeParams);
        Assert.DoesNotContain(query, q => q.StartsWith(expected + "=", StringComparison.Ordinal)); // bare, no value
    }

    [Fact]
    public void Fullscreen_is_not_a_protocol_mode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenClipUri.Image(CaptureMode.Fullscreen));
    }

    [Fact]
    public void Image_pins_api_version_and_user_agent_and_never_sends_redirect()
    {
        var uri = ScreenClipUri.Image(CaptureMode.Rectangle);

        Assert.Contains("&api-version=1.2", uri);
        Assert.Contains("&user-agent=WindowsCaptureController", uri);
        Assert.DoesNotContain("redirect-uri", uri);
    }

    [Fact]
    public void Enabled_modes_are_listed_explicitly_not_aggregate()
    {
        var uri = ScreenClipUri.Image(CaptureMode.Rectangle);

        Assert.Contains("enabledModes=RectangleSnip,WindowSnip,FreeformSnip,FullscreenSnip", uri);
        Assert.DoesNotContain("SnippingAllModes", uri);
    }

    [Fact]
    public void Auto_save_flag_is_optional()
    {
        Assert.EndsWith("&auto-save", ScreenClipUri.Image(CaptureMode.Window, autoSave: true));
        Assert.DoesNotContain("auto-save", ScreenClipUri.Image(CaptureMode.Window, autoSave: false));
    }

    [Fact]
    public void Video_has_no_mode_parameter_and_uses_new_protocol()
    {
        var uri = ScreenClipUri.Video();

        Assert.StartsWith("ms-screenclip://capture/video?", uri);
        Assert.DoesNotContain("rectangle&", uri);
        Assert.DoesNotContain("ms-screenclip:?", uri); // deprecated legacy form never produced
    }
}
