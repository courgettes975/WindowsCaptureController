using System.Diagnostics;
using System.Windows.Automation;
using WindowsCaptureController.Logging;
using WindowsCaptureController.Native;
using WindowsCaptureController.WindowsCapture;

namespace WindowsCaptureController.Automation;

public enum TriState { Unknown, On, Off }

/// <summary>Snapshot of the video recording UI (no user content: control presence, enabled flags, timer digits).</summary>
public sealed record RecordingSnapshot(CaptureAppState State, bool ControlsEnabled, string Timer, TriState Microphone, TriState SystemAudio);

/// <summary>
/// UI Automation of the Snipping Tool screen recorder, from the phase 5 probe (11.2609.15, fr-FR).
/// Cheap Win32 checks first (window classes), UI Automation only when a recording UI exists.
/// Never selects the recording area and never starts a recording that the user did not prepare.
/// </summary>
public static class RecordingAutomation
{
    /// <summary>True while the red frame around the recorded area exists (recording or paused).</summary>
    public static bool IsRecordingIndicatorPresent()
    {
        foreach (var p in SnippingToolLocator.GetProcesses())
        {
            using (p)
            {
                if (Win32.GetTopLevelWindows((uint)p.Id).Any(h => Win32.GetClassName(h) == SnippingToolUiMap.RecordingIndicatorClass))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static AutomationElement? FindById(string automationId)
    {
        foreach (var p in SnippingToolLocator.GetProcesses())
        {
            using (p)
            {
                try
                {
                    foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children,
                                 new PropertyCondition(AutomationElement.ProcessIdProperty, p.Id)))
                    {
                        var e = CaptureOverlayAutomation.ById(w, automationId);
                        if (e != null)
                        {
                            return e;
                        }
                    }
                }
                catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException)
                {
                    // window closing: ignore
                }
            }
        }

        return null;
    }

    /// <summary>Waits for an element to exist AND be enabled (Stop/Pause are disabled during the 3 s countdown).</summary>
    public static AutomationElement? WaitEnabled(string automationId, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        do
        {
            var e = FindById(automationId);
            if (e != null && e.Current.IsEnabled)
            {
                return e;
            }

            Thread.Sleep(150);
        }
        while (sw.ElapsedMilliseconds < timeoutMs);

        return null;
    }

    public static bool Invoke(AutomationElement? e, string what)
    {
        if (e == null || !e.TryGetCurrentPattern(InvokePattern.Pattern, out var p))
        {
            return false;
        }

        ((InvokePattern)p).Invoke();
        Log.Info($"Recording UI: {what} invoked ({e.Current.AutomationId})");
        return true;
    }

    /// <summary>
    /// Video state. <paramref name="detectPause"/> compares the timer digits 1.1 s apart (a frozen timer with
    /// enabled controls = paused); without it, a paused recording is reported as Recording.
    /// </summary>
    public static RecordingSnapshot Snapshot(bool detectPause)
    {
        if (IsRecordingIndicatorPresent())
        {
            var stop = FindById(SnippingToolUiMap.RecordStop);
            var enabled = stop?.Current.IsEnabled == true;
            var timer = ReadTimer(stop);
            var pause = FindById(SnippingToolUiMap.RecordPauseResume);
            var paused = ParsePausedName(pause?.Current.Name ?? "");
            var state = paused == TriState.On ? CaptureAppState.RecordingPaused : CaptureAppState.Recording;
            if (detectPause && enabled && paused == TriState.Unknown)
            {
                // Unknown wording (other language): fall back to a frozen timer over 1.1 s.
                Thread.Sleep(1100);
                if (timer.Length > 0 && ReadTimer(FindById(SnippingToolUiMap.RecordStop)) == timer)
                {
                    state = CaptureAppState.RecordingPaused;
                }
            }

            return new RecordingSnapshot(state, enabled, timer, MicState(FindById(SnippingToolUiMap.RecordMic), recordingToolbar: true),
                AudioState(FindById(SnippingToolUiMap.RecordSystemAudio)));
        }

        if (FindById(SnippingToolUiMap.RecordStartButton) is { } start)
        {
            return new RecordingSnapshot(CaptureAppState.VideoReady, start.Current.IsEnabled, "",
                MicState(FindById(SnippingToolUiMap.RecordMicBeforeStart), recordingToolbar: false),
                AudioState(FindById(SnippingToolUiMap.RecordSystemAudio)));
        }

        if (FindById(SnippingToolUiMap.VideoProgressSlider) != null)
        {
            return new RecordingSnapshot(CaptureAppState.VideoEditing, true, "", TriState.Unknown, TriState.Unknown);
        }

        var overlay = CaptureOverlayAutomation.FindToolbarWindow();
        if (overlay != null)
        {
            var toggle = CaptureOverlayAutomation.ById(overlay, SnippingToolUiMap.CaptureModeToggle);
            var video = toggle != null && toggle.TryGetCurrentPattern(TogglePattern.Pattern, out var tp) &&
                        ((TogglePattern)tp).Current.ToggleState == ToggleState.On;
            return new RecordingSnapshot(video ? CaptureAppState.VideoSelection : CaptureAppState.CaptureSelection, true, "", TriState.Unknown, TriState.Unknown);
        }

        return new RecordingSnapshot(CaptureAppState.Unknown, false, "", TriState.Unknown, TriState.Unknown);
    }

    /// <summary>The recorder timer is three unnamed Text siblings "00" ":" "00" ":" "03" next to the Stop button.</summary>
    private static string ReadTimer(AutomationElement? anchor)
    {
        try
        {
            var parent = anchor == null ? null : TreeWalker.ControlViewWalker.GetParent(anchor);
            if (parent == null)
            {
                return "";
            }

            var digits = parent.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                .Cast<AutomationElement>()
                .Select(t => t.Current.Name)
                .Where(n => n.Length == 2 && n.All(char.IsDigit));
            return string.Join(":", digits);
        }
        catch
        {
            return "";
        }
    }

    // The mic / audio buttons expose no Toggle pattern: their state is only in their (localized) name.
    // FR measured on this machine; EN wording NOT verified (state reported Unknown if no rule matches).
    // Recording toolbar: name describes the CURRENT state ("Micro désactivé. Sélectionnez… pour activer").
    // Pre-start toolbar: name describes the ACTION ("Désactiver le son" = currently on).
    internal static TriState MicState(AutomationElement? e, bool recordingToolbar) =>
        e == null ? TriState.Unknown : ParseMicName(e.Current.Name, recordingToolbar);

    internal static TriState ParseMicName(string name, bool recordingToolbar)
    {
        var n = name.ToLowerInvariant();
        if (recordingToolbar)
        {
            if (n.StartsWith("micro désactivé") || n.StartsWith("microphone off") || n.Contains("microphone is off"))
            {
                return TriState.Off;
            }

            if (n.StartsWith("micro activé") || n.StartsWith("microphone on") || n.Contains("microphone is on"))
            {
                return TriState.On;
            }

            return TriState.Unknown;
        }

        if (n.StartsWith("désactiver") || n.StartsWith("mute"))
        {
            return TriState.On;
        }

        if (n.StartsWith("activer") || n.StartsWith("unmute"))
        {
            return TriState.Off;
        }

        return TriState.Unknown;
    }

    /// <summary>
    /// PauseResumeButton name (measured fr-FR): running = "Suspendre l’enregistrement";
    /// paused = "Enregistrement des résumés" (sic: the Snipping Tool's French translation of "Resume recording").
    /// Returns On = paused, Off = running, Unknown = wording not recognised.
    /// </summary>
    internal static TriState ParsePausedName(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        if (n.Length == 0)
        {
            return TriState.Unknown;
        }

        if (n.StartsWith("suspendre") || n.StartsWith("pause"))
        {
            return TriState.Off;
        }

        if (n.StartsWith("reprendre") || n.StartsWith("resume") || n.Contains("résumé") || n.Contains("resume"))
        {
            return TriState.On;
        }

        return TriState.Unknown;
    }

    internal static TriState AudioState(AutomationElement? e) => e == null ? TriState.Unknown : ParseSystemAudioName(e.Current.Name);

    /// <summary>"Audio système activé. Sélectionnez … pour désactiver…" (measured) — the first sentence is the state.</summary>
    internal static TriState ParseSystemAudioName(string name)
    {
        var first = name.Split('.')[0].ToLowerInvariant();
        if (first.Contains("désactivé") || first.EndsWith(" off") || first.Contains("disabled"))
        {
            return TriState.Off;
        }

        if (first.Contains("activé") || first.EndsWith(" on") || first.Contains("enabled"))
        {
            return TriState.On;
        }

        return TriState.Unknown;
    }
}
