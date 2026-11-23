using System.Diagnostics;
using WindowsCaptureController.Logging;
using WindowsCaptureController.Native;

namespace WindowsCaptureController.WindowsCapture;

/// <summary>What became the foreground window after a capture request.</summary>
public sealed record OverlayInfo(string ProcessName, string ClassName, string Bounds, long ElapsedMs);

/// <summary>
/// Confirms that a capture request produced the Snipping Tool selection overlay, by watching which
/// process owns the foreground window. Read-only (no input is sent unless <see cref="CancelOverlay"/> is called).
/// </summary>
public static class OverlayWatcher
{
    /// <summary>Processes allowed to own the capture overlay (confirmed by the phase 2 machine test).</summary>
    public static readonly string[] OverlayProcesses = { "SnippingTool", "ScreenClippingHost" };

    public static OverlayInfo? WaitForOverlay(IntPtr previousForeground, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var name = WindowActivator.ForegroundProcessName(out var h);
            if (h != IntPtr.Zero && h != previousForeground && OverlayProcesses.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                Win32.GetWindowRect(h, out var r);
                var info = new OverlayInfo(name, Win32.GetClassName(h), r.ToString(), sw.ElapsedMilliseconds);
                Log.Info($"Capture overlay detected: process={info.ProcessName} class={info.ClassName} bounds={info.Bounds} after {info.ElapsedMs} ms");
                return info;
            }

            Thread.Sleep(50);
        }

        Log.Warning($"No capture overlay detected within {timeoutMs} ms.");
        return null;
    }

    /// <summary>
    /// Sends Escape ONLY if the foreground window belongs to the Snipping Tool overlay process.
    /// Used by tests to close the overlay without taking a capture.
    /// </summary>
    public static bool CancelOverlay()
    {
        var name = WindowActivator.ForegroundProcessName(out _);
        if (!OverlayProcesses.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            Log.Warning($"Escape not sent: foreground process is '{name}', not the capture overlay.");
            return false;
        }

        var ok = KeyboardInput.SendChord(KeyboardInput.VK_ESCAPE);
        Log.Info($"Escape sent to capture overlay ({name}): {ok}");
        return ok;
    }
}
