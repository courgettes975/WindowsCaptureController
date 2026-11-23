#nullable enable
using System.Collections.Generic;
using System.Text.Json;

namespace WindowsCaptureController.Shared;

// Line-delimited JSON protocol between the Logi plugin (client) and WccAgent.exe (server).
// One request per line on the agent's stdin, one response per line on its stdout.
// Same design as MXTeamViewer (proven on this machine). No user content ever travels here:
// no clipboard, no image, no OCR text — only commands, statuses and short messages.
public sealed class AgentRequest
{
    public long Id { get; set; }
    public string Cmd { get; set; } = "";
    public Dictionary<string, string> Args { get; set; } = new();
}

public sealed class AgentResponse
{
    public long Id { get; set; }
    public bool Ok { get; set; }
    /// <summary>ActionStatus name: Success, Sent, Debounced, NotAvailable, NotImplementedYet, Failed.</summary>
    public string Status { get; set; } = "";
    /// <summary>ControlMethod name used by the agent.</summary>
    public string Method { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Hint { get; set; }
    public Dictionary<string, string> Data { get; set; } = new();
}

public static class AgentCommands
{
    public const string Ping = "ping";
    public const string State = "state";
    public const string Launch = "launch";
    public const string Front = "front";
    public const string NewCapture = "capture.new";
    public const string Rectangle = "capture.rectangle";
    public const string Window = "capture.window";
    public const string Freeform = "capture.freeform";
    public const string Fullscreen = "capture.fullscreen";
    public const string DefaultCapture = "capture.default";     // configured default mode (end of a delay)
    public const string MonitorCapture = "capture.monitor";     // args: mode, output, folder (optional)
    public const string OpenLastCapture = "last.capture";
    public const string CapturesFolder = "folder.captures";
    public const string VideosFolder = "folder.videos";
    public const string VideoMode = "video.open";
    public const string VideoStart = "video.start";
    public const string VideoStop = "video.stop";
    public const string VideoPause = "video.pause";          // toggle pause / resume
    public const string VideoMic = "video.mic";
    public const string VideoDiscard = "video.discard";      // args: confirmed=true (required)
    public const string VideoSaveAs = "video.saveas";       // video editor "Enregistrer sous" (saveButton)
    public const string VideoState = "video.state";

    // Dial (phase 7)
    public const string ConfigGet = "config.get";        // data: defaultDialMode, dialDirection, dialSensitivity, captureDelaySeconds
    public const string HistoryList = "history.list";    // args: max ; data: count, "0".."n-1" = kind|yyyy-MM-dd HH:mm|name
    public const string HistoryOpen = "history.open";    // args: name (bare file name)

    // Image editor (phase 6)
    public const string EditState = "edit.state";       // data: open, tool, undo, redo
    public const string EditPen = "edit.pen";
    public const string EditHighlighter = "edit.highlighter";
    public const string EditEraser = "edit.eraser";
    public const string EditUndo = "edit.undo";
    public const string EditRedo = "edit.redo";
    public const string EditCopy = "edit.copy";
    public const string EditSaveAs = "edit.saveas";
    public const string EditShare = "edit.share";
    public const string EditRuler = "edit.ruler";
    public const string EditClearAll = "edit.clearall";  // args: confirmed=true (required)
    public const string EditColor = "edit.color";        // args: index | delta
    public const string EditWidth = "edit.width";        // args: value | delta | reset=true
    public const string ZoomIn = "zoom.in";
    public const string ZoomOut = "zoom.out";
    public const string ZoomReset = "zoom.100";
    public const string ZoomFit = "zoom.fit";          // data: state, enabled, timer, mic, audio
    public const string OpenLastVideo = "last.video";
}

public static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
