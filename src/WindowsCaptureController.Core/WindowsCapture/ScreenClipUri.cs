using System.Text;

namespace WindowsCaptureController.WindowsCapture;

/// <summary>
/// Builds requests of the documented Snipping Tool protocol (api-version 1.2):
/// https://learn.microsoft.com/windows/apps/develop/launch/launch-snipping-tool
///   ms-screenclip://capture/image?{rectangle|freeform|window}[&amp;enabledModes=...][&amp;auto-save]
///   ms-screenclip://capture/video[...]
/// Rules from the documentation, enforced here:
///   * /image needs exactly ONE bare mode parameter (rectangle, freeform or window).
///   * Fullscreen is NOT a mode parameter; it can only be offered through enabledModes=FullscreenSnip.
///   * /video is always rectangle; mode parameters are ignored, so none is sent.
///   * No redirect-uri: an unpackaged caller cannot receive the response (documented limitation).
/// The deprecated legacy "ms-screenclip:" form is never produced.
/// </summary>
public static class ScreenClipUri
{
    public const string ApiVersion = "1.2";
    public const string UserAgent = "WindowsCaptureController";

    /// <summary>All image modes, listed explicitly (aggregate values may change between releases).</summary>
    public const string AllImageModes = "RectangleSnip,WindowSnip,FreeformSnip,FullscreenSnip";

    public static string Image(CaptureMode initialMode, string? enabledModes = AllImageModes, bool autoSave = true)
    {
        var mode = initialMode switch
        {
            CaptureMode.Rectangle => "rectangle",
            CaptureMode.Window => "window",
            CaptureMode.Freeform => "freeform",
            _ => throw new ArgumentOutOfRangeException(nameof(initialMode), initialMode,
                "Le plein écran n'est pas un mode du protocole ms-screenclip (seulement via enabledModes)."),
        };

        var sb = new StringBuilder("ms-screenclip://capture/image?").Append(mode);
        AppendCommon(sb, enabledModes, autoSave);
        return sb.ToString();
    }

    public static string Video(bool autoSave = true)
    {
        var sb = new StringBuilder("ms-screenclip://capture/video?enabledModes=RectangleRecord");
        sb.Append("&api-version=").Append(ApiVersion);
        sb.Append("&user-agent=").Append(UserAgent);
        if (autoSave)
        {
            sb.Append("&auto-save");
        }

        return sb.ToString();
    }

    private static void AppendCommon(StringBuilder sb, string? enabledModes, bool autoSave)
    {
        if (!string.IsNullOrWhiteSpace(enabledModes))
        {
            sb.Append("&enabledModes=").Append(enabledModes);
        }

        sb.Append("&api-version=").Append(ApiVersion);
        sb.Append("&user-agent=").Append(UserAgent);
        if (autoSave)
        {
            // Respects the user's Snipping Tool setting: no file is written if auto-save is disabled there.
            sb.Append("&auto-save");
        }
    }
}
