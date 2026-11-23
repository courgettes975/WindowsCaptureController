using System.Diagnostics;
using Windows.Management.Deployment;
using WindowsCaptureController.Logging;
using WindowsCaptureController.Native;

namespace WindowsCaptureController.WindowsCapture;

/// <summary>Snipping Tool package identity, as reported by the documented PackageManager API.</summary>
public sealed record SnippingToolPackage(string FullName, string Version, string InstallPath, string Architecture);

/// <summary>A top-level window of the Snipping Tool process.</summary>
public sealed record SnippingToolWindow(IntPtr Handle, uint ProcessId, string ClassName, string Title, bool Minimized, string Bounds);

/// <summary>Finds the installed Snipping Tool package and its running windows. Read-only.</summary>
public static class SnippingToolLocator
{
    public const string PackageFamilyName = "Microsoft.ScreenSketch_8wekyb3d8bbwe";
    public const string AppUserModelId = PackageFamilyName + "!App";
    public const string ProcessName = "SnippingTool";

    public static SnippingToolPackage? FindPackage()
    {
        try
        {
            var pm = new PackageManager();
            var pkg = pm.FindPackagesForUser(string.Empty, PackageFamilyName).FirstOrDefault();
            if (pkg == null)
            {
                return null;
            }

            var v = pkg.Id.Version;
            return new SnippingToolPackage(
                pkg.Id.FullName,
                $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                pkg.InstalledLocation?.Path ?? string.Empty,
                pkg.Id.Architecture.ToString());
        }
        catch (Exception ex)
        {
            Log.Warning($"Snipping Tool package lookup failed: {ex.GetType().Name}");
            return null;
        }
    }

    public static IReadOnlyList<Process> GetProcesses() => Process.GetProcessesByName(ProcessName);

    /// <summary>All visible, un-owned top-level windows of every SnippingTool.exe process.</summary>
    public static IReadOnlyList<SnippingToolWindow> GetWindows()
    {
        var list = new List<SnippingToolWindow>();
        foreach (var p in GetProcesses())
        {
            using (p)
            {
                foreach (var h in Win32.GetTopLevelWindows((uint)p.Id))
                {
                    Win32.GetWindowRect(h, out var r);
                    list.Add(new SnippingToolWindow(h, (uint)p.Id, Win32.GetClassName(h), Win32.GetWindowText(h), Win32.IsIconic(h), r.ToString()));
                }
            }
        }

        return list;
    }

    /// <summary>
    /// The application (editor) window. Measured on 11.2609.15: class "XamlWindow" with a non-empty title
    /// (the title is localized, so it is never compared to a fixed string).
    /// </summary>
    public static SnippingToolWindow? FindMainWindow() =>
        GetWindows()
            .Where(w => w.ClassName == "XamlWindow" && w.Title.Length > 0)
            .OrderBy(w => w.Minimized) // prefer a restored window
            .FirstOrDefault();
}
