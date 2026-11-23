namespace WindowsCaptureController.Automation;

/// <summary>
/// SINGLE place where Snipping Tool UI Automation identifiers live (fragile by nature: may change with
/// a Snipping Tool update). Measured on Microsoft.ScreenSketch 11.2609.15.0, Windows 11 build 26300, fr-FR.
/// AutomationIds are language independent; localized names are never compared.
/// </summary>
public static class SnippingToolUiMap
{
    public const string MeasuredOnVersion = "11.2609.15.0";

    // ---- Capture overlay (separate SnippingTool.exe process started by Win+Shift+S)
    public const string OverlayToolbar = "ScreenshotOverlayToolbar";
    public const string OverlaySelectionRectangle = "SelectionRectangle";
    public const string OverlayCloseButton = "CloseButton";
    public const string OverlayLiveAnnotationToggle = "LiveAnnotationModeToggleButton";

    // ---- Shared by overlay and main window
    public const string CaptureModeToggle = "CaptureModeToggleSwitch"; // Off = image, On = video
    public const string SnippingModeCombo = "SnippingModeComboBox";
    public const string SnipDelayCombo = "SnipDelayComboBox";

    /// <summary>Item order of <see cref="SnippingModeCombo"/> (image mode), measured: Rectangle, Window, Full screen, Freeform.</summary>
    public const int ModeIndexRectangle = 0;
    public const int ModeIndexWindow = 1;
    public const int ModeIndexFullscreen = 2;
    public const int ModeIndexFreeform = 3;
    public const int ImageModeCount = 4;

    // ---- Video (measured in the phase 5 probe, 2026-11-23)
    public const string RecordingSelectionRect = "RecordingSelectionRect";
    /// <summary>Appears once the user has drawn the recording area (before Start).</summary>
    public const string RecordingOverlayToolbar = "RecordingOverlayToolbar";
    public const string RecordStartButton = "StartButton";
    /// <summary>Microphone toggle in the pre-recording toolbar.</summary>
    public const string RecordMicBeforeStart = "MultiMicrophoneToggleButton";
    /// <summary>Microphone toggle in the recording toolbar.</summary>
    public const string RecordMic = "MicrophoneToggleButton";
    public const string RecordSystemAudio = "SystemAudioToggleButton";
    /// <summary>Disabled during the 3 s countdown that follows Start.</summary>
    public const string RecordPauseResume = "PauseResumeButton";
    /// <summary>Disabled during the 3 s countdown that follows Start.</summary>
    public const string RecordStop = "StopButton";
    /// <summary>"Annuler l'enregistrement" (before/during the countdown).</summary>
    public const string RecordCancel = "CancelButton";
    /// <summary>"Abandonner l'enregistrement" (after the countdown): no confirmation dialog, data lost.</summary>
    public const string RecordDiscard = "DiscardButton";
    /// <summary>Win32 class of the red frame shown around the area while recording.</summary>
    public const string RecordingIndicatorClass = "RecordingAreaIndicatorWindow";
    public const int RecordCountdownMs = 3000;

    // ---- Video editor (main window after Stop — second probe, 14:03). NOTE the lower-case ids,
    // different from the image editor (SaveBarButton / CopyBarButton).
    public const string VideoProgressSlider = "ProgressSlider";
    public const string VideoPlayPause = "PlayPauseButtonOnLeft";
    public const string VideoTrim = "TrimButton";
    public const string VideoEditInClipchamp = "EditInClipchampButton";
    public const string VideoSaveAs = "saveButton";
    public const string VideoCopy = "copyButton";

    // ---- Main window
    public const string NewCaptureButton = "NewCaptureButton";
    public const string TextExtractorButton = "LaunchTextExtractorButton";

    // ---- Editor (phase 6 probe, 14:09 — synthetic image)
    /// <summary>Colour list inside the pen / highlighter flyout (30 pen colours, 6 highlighter colours).</summary>
    public const string ColorPalette = "PenColorPalette";
    /// <summary>Same id for pen (1..24, default 4) and highlighter (12..64, default 12).</summary>
    public const string StrokeWidthSlider = "PenStrokeWidthSlider";
    public const double PenDefaultWidth = 4;
    public const double HighlighterDefaultWidth = 12;
    /// <summary>Eraser flyout: "Effacer toutes les marques" (destructive: confirmation required).</summary>
    public const string EraserClearAll = "InkToolbarClearAll";
    public const string MoreOptionsButton = "AppBarMoreOptionsButton";
    public const string OverflowPopup = "OverflowPopup";
    public const string ZoomIn = "ZoomInMenuItem";
    public const string ZoomOut = "ZoomOutMenuItem";
    public const string ZoomActualSize = "ZoomToActualSizeMenuItem";
    public const string ZoomFit = "ZoomFitToWindow";
    /// <summary>Overflow menu: show / hide the ruler. No protractor in this version. Ruler not exposed for rotation.</summary>
    public const string RulerButton = "RulerButton";
    /// <summary>NEVER invoked by the plugin: sends the image to an online service (Bing).</summary>
    public const string VisualSearchForbidden = "VisualSearchButton";
    public const string EditInPaintButton = "EditInPaintButton";
    public const string OpenScreenshotsFolderButton = "OpenScreenshotsFolderBarButton";

    public const string PenButton = "InkToolbarBallpointPenButton";
    public const string HighlighterButton = "InkToolbarHighlighterButton";
    public const string EraserButton = "InkToolbarEraserButton";
    public const string PenWidthSlider = "PenStrokeWidthSlider";
    public const string UndoButton = "UndoBarButton";
    public const string RedoButton = "RedoBarButton";
    public const string CopyButton = "CopyBarButton";
    public const string SaveAsButton = "SaveBarButton";
    public const string ShareButton = "ShareBarButton";
    public const string OcrToggle = "OcrOverlayButton";
    public const string OcrCopyAllText = "CopyAllTextFromOCRButton";
    public const string QuickRedact = "QuickRedactButton";
    public const string AnnotateCanvas = "AnnotateCanvas";
}
