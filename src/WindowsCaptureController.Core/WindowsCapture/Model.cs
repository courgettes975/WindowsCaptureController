namespace WindowsCaptureController.WindowsCapture;

/// <summary>Interactive capture modes of the Snipping Tool (names match the documented URI parameters).</summary>
public enum CaptureMode
{
    Rectangle,
    Window,
    Freeform,
    Fullscreen,
}

/// <summary>States of the Snipping Tool, as defined in the feasibility report (section 7).</summary>
public enum CaptureAppState
{
    Unknown,
    Closed,
    Idle,
    CaptureSelection,
    ImageEditing,
    VideoSelection,
    VideoReady,
    Recording,
    RecordingPaused,
    VideoEditing,
    Saving,
}

/// <summary>How an action was carried out (logged, shown in diagnostics).</summary>
public enum ControlMethod
{
    None,
    PackageActivation,
    Win32Window,
    DocumentedUri,
    WindowsShortcut,
    UiAutomation,
    SnippingToolShortcut,
    IndependentCapture,
    Shell,
}

public enum ActionStatus
{
    Success,
    /// <summary>Request accepted but not yet confirmed (e.g. overlay not detected in time).</summary>
    Sent,
    Debounced,
    NotAvailable,
    NotImplementedYet,
    Failed,
}

/// <summary>Result of every controller call: human readable, loggable, never contains user content.</summary>
public sealed record ActionResult(ActionStatus Status, ControlMethod Method, string Message, string? Hint = null)
{
    public bool IsOk => this.Status is ActionStatus.Success or ActionStatus.Sent;

    public static ActionResult Ok(ControlMethod method, string message) => new(ActionStatus.Success, method, message);

    public static ActionResult Sent(ControlMethod method, string message, string? hint = null) =>
        new(ActionStatus.Sent, method, message, hint);

    public static ActionResult Fail(ControlMethod method, string message, string? hint = null) =>
        new(ActionStatus.Failed, method, message, hint);

    public static ActionResult Unavailable(string message, string? hint = null) =>
        new(ActionStatus.NotAvailable, ControlMethod.None, message, hint);

    public static ActionResult Later(string feature, int phase) =>
        new(ActionStatus.NotImplementedYet, ControlMethod.None, $"{feature} : prévu en phase {phase}.");

    public static ActionResult Debounced() =>
        new(ActionStatus.Debounced, ControlMethod.None, "Appui ignoré (anti-rebond).");

    public override string ToString() =>
        $"{this.Status} via {this.Method}: {this.Message}" + (this.Hint == null ? string.Empty : $" — {this.Hint}");
}
