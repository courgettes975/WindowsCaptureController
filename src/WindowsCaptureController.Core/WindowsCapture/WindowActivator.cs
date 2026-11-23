using WindowsCaptureController.Logging;
using WindowsCaptureController.Native;

namespace WindowsCaptureController.WindowsCapture;

/// <summary>Restores a window and brings it to the foreground with documented Win32 calls.</summary>
public static class WindowActivator
{
    public static bool BringToFront(IntPtr hWnd)
    {
        if (!Win32.IsWindow(hWnd))
        {
            return false;
        }

        if (Win32.IsIconic(hWnd))
        {
            Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
        }

        if (Win32.SetForegroundWindow(hWnd) && Win32.GetForegroundWindow() == hWnd)
        {
            return true;
        }

        // Windows refuses SetForegroundWindow from a background process (foreground lock).
        // Documented workaround: share the input state of the current foreground thread for the call.
        var fg = Win32.GetForegroundWindow();
        var fgThread = Win32.GetWindowThreadProcessId(fg, out _);
        var me = Win32.GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != me && Win32.AttachThreadInput(me, fgThread, true);
        try
        {
            Win32.BringWindowToTop(hWnd);
            Win32.ShowWindow(hWnd, Win32.SW_SHOW);
            Win32.SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached)
            {
                Win32.AttachThreadInput(me, fgThread, false);
            }
        }

        var ok = Win32.GetForegroundWindow() == hWnd;
        if (!ok)
        {
            Log.Warning("Foreground activation refused by Windows (window restored but not focused).");
        }

        return ok;
    }

    /// <summary>Handle of the current foreground window.</summary>
    public static IntPtr ForegroundWindow => Win32.GetForegroundWindow();

    /// <summary>Process name of the current foreground window ("" if none).</summary>
    public static string ForegroundProcessName(out IntPtr hWnd)
    {
        hWnd = Win32.GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)Win32.GetProcessId(hWnd));
            return p.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
