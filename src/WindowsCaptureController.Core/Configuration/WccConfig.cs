using System.Text.Json.Serialization;

namespace WindowsCaptureController.Configuration;

/// <summary>
/// %LOCALAPPDATA%\WindowsCaptureController\config.json. Every value is validated by <see cref="ConfigStore"/>:
/// an invalid value is replaced by its safe default and a WARNING is logged (the plugin never crashes on config).
/// </summary>
public sealed class WccConfig
{
    /// <summary>"AUTO" (Windows Screenshots known folder) or an absolute folder path. Environment variables allowed.</summary>
    [JsonPropertyName("captureFolder")] public string CaptureFolder { get; set; } = "AUTO";

    /// <summary>"AUTO" (Videos\Screen Recordings) or an absolute folder path.</summary>
    [JsonPropertyName("videoFolder")] public string VideoFolder { get; set; } = "AUTO";

    /// <summary>rectangle | window | freeform — mode used by "Nouvelle capture" after a delay.</summary>
    [JsonPropertyName("defaultCaptureMode")] public string DefaultCaptureMode { get; set; } = "rectangle";

    /// <summary>zoom | thickness | color | history | delay (Dialpad, phase 7).</summary>
    [JsonPropertyName("defaultDialMode")] public string DefaultDialMode { get; set; } = "zoom";

    /// <summary>normal | inverted (Dialpad rotation direction).</summary>
    [JsonPropertyName("dialDirection")] public string DialDirection { get; set; } = "normal";

    /// <summary>Dial ticks per step, 1 (most sensitive) … 10.</summary>
    [JsonPropertyName("dialSensitivity")] public int DialSensitivity { get; set; } = 1;

    [JsonPropertyName("autoSave")] public bool AutoSave { get; set; }

    /// <summary>png | jpg (independent capture module).</summary>
    [JsonPropertyName("autoSaveImageFormat")] public string AutoSaveImageFormat { get; set; } = "png";

    /// <summary>0 | 3 | 5 | 10 (values offered by the installed Snipping Tool).</summary>
    [JsonPropertyName("captureDelaySeconds")] public int CaptureDelaySeconds { get; set; }

    /// <summary>
    /// all-screens | primary | pointer-monitor | active-monitor. Default all-screens (+ file) = documented
    /// Win+PrtScn, as validated in phase 1. The other values use the independent capture module.
    /// </summary>
    [JsonPropertyName("multiMonitorMode")] public string MultiMonitorMode { get; set; } = "all-screens";

    /// <summary>Where the full-screen capture goes: file | clipboard | both.</summary>
    [JsonPropertyName("fullscreenOutput")] public string FullscreenOutput { get; set; } = "file";

    [JsonPropertyName("clipboardFeedbackDurationMs")] public int ClipboardFeedbackDurationMs { get; set; } = 1500;

    [JsonPropertyName("enableContextProfiles")] public bool EnableContextProfiles { get; set; } = true;

    [JsonPropertyName("enableOcrActions")] public bool EnableOcrActions { get; set; } = true;

    [JsonPropertyName("enableSensitiveDataActions")] public bool EnableSensitiveDataActions { get; set; }

    /// <summary>snipping-tool | windows | disabled.</summary>
    [JsonPropertyName("microphoneControlMode")] public string MicrophoneControlMode { get; set; } = "snipping-tool";

    /// <summary>auto | fr | en.</summary>
    [JsonPropertyName("uiLanguage")] public string UiLanguage { get; set; } = "auto";

    /// <summary>trace | debug | info | warning | error.</summary>
    [JsonPropertyName("loggingLevel")] public string LoggingLevel { get; set; } = "info";

    public static readonly string[] CaptureModes = { "rectangle", "window", "freeform" };
    public static readonly string[] DialModes = { "zoom", "thickness", "color", "history", "delay" };
    public static readonly string[] DialDirections = { "normal", "inverted" };
    public static readonly string[] ImageFormats = { "png", "jpg" };
    public static readonly int[] Delays = { 0, 3, 5, 10 };
    public static readonly string[] MonitorModes = { "all-screens", "primary", "pointer-monitor", "active-monitor" };
    public static readonly string[] Outputs = { "file", "clipboard", "both" };
    public static readonly string[] MicrophoneModes = { "snipping-tool", "windows", "disabled" };
    public static readonly string[] Languages = { "auto", "fr", "en" };
    public static readonly string[] LogLevels = { "trace", "debug", "info", "warning", "error" };
}
