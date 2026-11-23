using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WindowsCaptureController.Logging;
using WindowsCaptureController.Native;
using WindowsCaptureController.Storage;

namespace WindowsCaptureController.Capture;

/// <summary>A monitor in physical pixels (the agent is PerMonitorV2 DPI aware).</summary>
public sealed record MonitorInfo(string Device, Rectangle Bounds, bool Primary);

/// <summary>
/// Independent full-screen capture, used only for what the Snipping Tool cannot do: one specific monitor
/// (primary / under the pointer / holding the active window). Local only: the image goes to a file and/or
/// the clipboard, never anywhere else, and is never logged.
/// </summary>
public static class ScreenCapture
{
    public static List<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Win32.RECT _, IntPtr _) =>
        {
            var info = new Win32.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32.MONITORINFOEX>() };
            if (Win32.GetMonitorInfo(h, ref info))
            {
                var r = info.rcMonitor;
                list.Add(new MonitorInfo(info.szDevice, Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), (info.dwFlags & 1) != 0));
            }

            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// Pure selection logic (unit tested). Returns the rectangle to capture for a multiMonitorMode:
    /// all-screens = union of monitors; primary; pointer-monitor; active-monitor (largest overlap with the
    /// active window, falling back to the pointer monitor, then the primary).
    /// </summary>
    public static Rectangle? SelectArea(string mode, IReadOnlyList<MonitorInfo> monitors, Point pointer, Rectangle? activeWindow)
    {
        if (monitors.Count == 0)
        {
            return null;
        }

        var primary = monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
        MonitorInfo? ByPointer() => monitors.FirstOrDefault(m => m.Bounds.Contains(pointer));

        switch (mode)
        {
            case "all-screens":
                return monitors.Select(m => m.Bounds).Aggregate(Rectangle.Union);
            case "primary":
                return primary.Bounds;
            case "pointer-monitor":
                return (ByPointer() ?? primary).Bounds;
            case "active-monitor":
                if (activeWindow is { Width: > 0, Height: > 0 } aw)
                {
                    var best = monitors
                        .Select(m => (m, area: Area(Rectangle.Intersect(m.Bounds, aw))))
                        .OrderByDescending(x => x.area)
                        .First();
                    if (best.area > 0)
                    {
                        return best.m.Bounds;
                    }
                }

                return (ByPointer() ?? primary).Bounds;
            default:
                return null;
        }
    }

    private static long Area(Rectangle r) => r.IsEmpty ? 0 : (long)r.Width * r.Height;

    public static Point PointerPosition() => Win32.GetCursorPos(out var p) ? new Point(p.X, p.Y) : Point.Empty;

    /// <summary>Bounds of the foreground window, or null if none (desktop / no window).</summary>
    public static Rectangle? ActiveWindowBounds()
    {
        var h = Win32.GetForegroundWindow();
        if (h == IntPtr.Zero || !Win32.GetWindowRect(h, out var r))
        {
            return null;
        }

        var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return rect.Width > 0 && rect.Height > 0 ? rect : null;
    }

    public static Bitmap CaptureArea(Rectangle area)
    {
        var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>Saves to folder with a local-time name such as Capture_2026-11-23_11-30-45.png (unique).</summary>
    public static string Save(Bitmap bmp, string folder, string format, DateTime now)
    {
        Directory.CreateDirectory(folder);
        var ext = format == "jpg" ? ".jpg" : ".png";
        var path = CaptureFiles.UniquePath(folder, CaptureFiles.MakeFileName("Capture", now, ext));
        bmp.Save(path, format == "jpg" ? ImageFormat.Jpeg : ImageFormat.Png);
        return path;
    }

    /// <summary>
    /// Puts the image on the clipboard from a dedicated STA thread, retrying while another process holds the
    /// clipboard. The previous clipboard content is only replaced when a valid image exists.
    /// </summary>
    public static bool CopyToClipboard(Bitmap bmp, int attempts = 5)
    {
        var ok = false;
        var t = new Thread(() =>
        {
            for (var i = 0; i < attempts && !ok; i++)
            {
                try
                {
                    System.Windows.Forms.Clipboard.SetImage(bmp);
                    ok = System.Windows.Forms.Clipboard.ContainsImage();
                }
                catch (ExternalException)
                {
                    Thread.Sleep(100); // clipboard busy
                }
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(3000);
        if (!ok)
        {
            Log.Warning("Clipboard busy: image not copied");
        }

        return ok;
    }
}
