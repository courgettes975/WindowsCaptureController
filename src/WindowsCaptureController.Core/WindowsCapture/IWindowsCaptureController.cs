namespace WindowsCaptureController.WindowsCapture;

/// <summary>
/// Abstraction over the Windows Snipping Tool. Every member returns an <see cref="ActionResult"/>
/// (never throws to the caller). Members not available in the installed version return NotAvailable;
/// members planned for a later phase return NotImplementedYet. Nothing is faked.
/// </summary>
public interface IWindowsCaptureController
{
    // --- Application
    ActionResult Launch();
    ActionResult BringToFront();
    CaptureAppState GetApplicationState();

    // --- Image capture
    ActionResult StartNewCapture();
    ActionResult StartRectangularCapture();
    ActionResult StartWindowCapture();
    ActionResult StartFreeformCapture();
    ActionResult StartFullscreenCapture();
    ActionResult StartDelayedCapture(int seconds);

    // --- Video
    ActionResult OpenVideoMode();
    ActionResult StartRecording();
    ActionResult PauseRecording();
    ActionResult ResumeRecording();
    ActionResult StopRecording();
    CaptureAppState GetRecordingState();

    // --- Annotation
    ActionResult SelectPen();
    ActionResult SelectHighlighter();
    ActionResult SelectEraser();
    ActionResult SelectRuler();
    ActionResult SelectProtractor();
    ActionResult Undo();
    ActionResult Redo();
    string? GetActiveTool();

    // --- Output
    ActionResult Copy();
    ActionResult Save();
    ActionResult SaveAs();
    ActionResult Share();

    // --- Text
    ActionResult ExtractText();
    ActionResult OpenTextActions();
    ActionResult RedactSensitiveInformation();

    // --- Zoom
    ActionResult ZoomIn();
    ActionResult ZoomOut();
    ActionResult ResetZoom();
    ActionResult FitToWindow();
    int? GetZoomLevel();

    // --- Files
    ActionResult OpenCapturesFolder();
    ActionResult OpenVideosFolder();
    ActionResult OpenLastCapture();
    ActionResult OpenLastVideo();
}
