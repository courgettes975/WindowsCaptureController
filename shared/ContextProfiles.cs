#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace WindowsCaptureController.Shared;

/// <summary>One action a contextual key can take (pure data, unit tested).</summary>
public sealed record ContextAction(string Id, string Label, string? Command, string Glyph,
    IReadOnlyDictionary<string, string>? Args = null, bool Confirm = false, bool DisplayOnly = false);

/// <summary>
/// "Contexte 1…9" keys: what each slot does for each Snipping Tool state (cahier des charges, profil contextuel).
/// The Logi SDK cannot rearrange a profile or switch profiles, so these 9 keys change their function and icon
/// with the state reported by the agent ("app.state"). With enableContextProfiles=false, the Closed profile
/// is always used (manual pages = the regular, non-contextual actions).
/// </summary>
public static class ContextProfiles
{
    public const int Slots = 9;

    private static Dictionary<string, string> A(params (string K, string V)[] kv) => kv.ToDictionary(x => x.K, x => x.V);

    public static readonly IReadOnlyDictionary<string, ContextAction> Actions = new[]
    {
        new ContextAction("open", "Capture", AgentCommands.Launch, "Scissors"),
        new ContextAction("new", "Nouvelle", AgentCommands.NewCapture, "NewCapture"),
        new ContextAction("rectangle", "Rectangle", AgentCommands.Rectangle, "Rectangle"),
        new ContextAction("window", "Fenêtre", AgentCommands.Window, "Window"),
        new ContextAction("freeform", "Forme libre", AgentCommands.Freeform, "Freeform"),
        new ContextAction("fullscreen", "Plein écran", AgentCommands.Fullscreen, "Fullscreen"),
        new ContextAction("video", "Vidéo", AgentCommands.VideoMode, "VideoCamera"),
        new ContextAction("last", "Dernière", AgentCommands.OpenLastCapture, "LastCapture"),
        new ContextAction("folder", "Captures", AgentCommands.CapturesFolder, "Folder"),
        new ContextAction("sel.close", "Fermer", AgentCommands.SelectionClose, "Cancel"),
        new ContextAction("info.area", "Tracez la zone", null, "Rectangle", DisplayOnly: true),
        new ContextAction("rec.start", "Enregistrer", AgentCommands.VideoStart, "RecordDot"),
        new ContextAction("rec.mic", "Micro", AgentCommands.VideoMic, "Microphone"),
        new ContextAction("rec.cancel", "Annuler", AgentCommands.VideoDiscard, "Cancel", A(("confirmed", "true"))),
        new ContextAction("rec.stop", "Arrêter", AgentCommands.VideoStop, "StopSquare"),
        new ContextAction("rec.pause", "Pause", AgentCommands.VideoPause, "PauseBars"),
        new ContextAction("rec.timer", "Durée", null, "Delay", DisplayOnly: true),
        new ContextAction("rec.discard", "Abandonner", AgentCommands.VideoDiscard, "Trash", A(("confirmed", "true")), Confirm: true),
        new ContextAction("video.play", "Lire", AgentCommands.VideoPlay, "Play"),
        new ContextAction("video.saveas", "Enreg. sous", AgentCommands.VideoSaveAs, "Save"),
        new ContextAction("video.copy", "Copier", AgentCommands.VideoCopy, "Copy"),
        new ContextAction("video.folder", "Vidéos", AgentCommands.VideosFolder, "VideoFolder"),
        new ContextAction("video.last", "Dern. vidéo", AgentCommands.OpenLastVideo, "LastVideo"),
        new ContextAction("pen", "Stylo", AgentCommands.EditPen, "Pen"),
        new ContextAction("highlighter", "Surligneur", AgentCommands.EditHighlighter, "Highlighter"),
        new ContextAction("eraser", "Gomme", AgentCommands.EditEraser, "Eraser"),
        new ContextAction("undo", "Annuler", AgentCommands.EditUndo, "Undo"),
        new ContextAction("redo", "Rétablir", AgentCommands.EditRedo, "Redo"),
        new ContextAction("copy", "Copier", AgentCommands.EditCopy, "Copy"),
        new ContextAction("saveas", "Enreg. sous", AgentCommands.EditSaveAs, "Save"),
        new ContextAction("share", "Partager", AgentCommands.EditShare, "Share"),
    }.ToDictionary(a => a.Id);

    /// <summary>Action ids per state; "" = empty slot. Every profile has exactly <see cref="Slots"/> entries.</summary>
    public static IReadOnlyList<string> For(string state, bool profilesEnabled = true)
    {
        if (!profilesEnabled)
        {
            state = "Closed";
        }

        string[] p = state switch
        {
            "CaptureSelection" => new[] { "rectangle", "window", "freeform", "sel.close", "", "", "", "", "" },
            "VideoSelection" => new[] { "info.area", "sel.close", "", "", "", "", "", "", "" },
            "VideoReady" => new[] { "rec.start", "rec.mic", "rec.cancel", "", "", "", "", "", "" },
            "Recording" or "RecordingPaused" => new[] { "rec.stop", "rec.pause", "rec.mic", "rec.timer", "rec.discard", "", "", "", "" },
            "VideoEditing" => new[] { "video.play", "video.saveas", "video.copy", "video.folder", "video.last", "video", "new", "", "" },
            "ImageEditing" => new[] { "pen", "highlighter", "eraser", "undo", "redo", "copy", "saveas", "share", "new" },
            _ => new[] { "new", "rectangle", "window", "fullscreen", "freeform", "video", "last", "folder", "open" }, // Closed / Idle / Unknown
        };
        return p;
    }

    public static ContextAction? Resolve(string state, int slot, bool profilesEnabled = true)
    {
        if (slot < 1 || slot > Slots)
        {
            return null;
        }

        var id = For(state, profilesEnabled)[slot - 1];
        return id.Length > 0 && Actions.TryGetValue(id, out var a) ? a : null;
    }
}
