using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Windows.Devices.Enumeration;
using WindowsCaptureController.Native;
using WindowsCaptureController.Storage;
using WindowsCaptureController.WindowsCapture;

namespace WindowsCaptureController.Diagnostics;

/// <summary>
/// Read-only machine report. PRIVACY: no clipboard, no capture content, no window titles of other apps,
/// no user name (profile paths are shortened to %USERPROFILE%).
/// </summary>
public static class SystemDiagnostics
{
    /// <summary>Logitech USB id of the MX Creative Keypad (measured in phase 1; the SDK names it DeviceType.Loupedeck70).</summary>
    public const string KeypadHardwareId = "VID_046D&PID_C354";

    public static string BuildReport()
    {
        var sb = new StringBuilder();
        void Line(string k, object? v) => sb.AppendLine($"  {k,-28}: {v}");

        sb.AppendLine("SYSTÈME");
        using (var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            // ProductName still says "Windows 10" on Windows 11 (documented registry quirk): use the build number.
            _ = int.TryParse(cv?.GetValue("CurrentBuildNumber") as string, out var build);
            var edition = (cv?.GetValue("EditionID") as string) ?? string.Empty;
            Line("Windows", $"Windows {(build >= 22000 ? "11" : "10")} {edition} {cv?.GetValue("DisplayVersion")} build {build}.{cv?.GetValue("UBR")}");
        }

        Line("Langue de l'interface", CultureInfo.CurrentUICulture.Name);
        Line("Architecture", RuntimeInformation.OSArchitecture);
        var monitors = GetMonitors();
        Line("Écrans", monitors.Count);
        foreach (var m in monitors)
        {
            sb.AppendLine($"    - {m}");
        }

        sb.AppendLine();
        sb.AppendLine("OUTIL CAPTURE");
        var pkg = SnippingToolLocator.FindPackage();
        Line("Installé", pkg != null ? "oui" : "NON");
        if (pkg != null)
        {
            Line("Version", pkg.Version);
            Line("Package", pkg.FullName);
        }

        Line("Processus", SnippingToolLocator.GetProcesses().Count);
        foreach (var w in SnippingToolLocator.GetWindows())
        {
            sb.AppendLine($"    - fenêtre class={w.ClassName} minimisée={w.Minimized} rect={w.Bounds}");
        }

        Line("État", new WindowsCaptureController.WindowsCapture.WindowsCaptureController().GetApplicationState());
        Line("Protocole ms-screenclip", Registry.ClassesRoot.OpenSubKey("ms-screenclip") != null ? "enregistré" : "absent");

        sb.AppendLine();
        sb.AppendLine("DOSSIERS (Known Folders)");
        Line("Captures d'écran", Mask(KnownFolders.ScreenshotsPath) + Exists(KnownFolders.ScreenshotsPath));
        Line("Vidéos", Mask(KnownFolders.VideosPath) + Exists(KnownFolders.VideosPath));
        Line("Enregistrements d'écran", Mask(KnownFolders.ScreenRecordingsPath) + Exists(KnownFolders.ScreenRecordingsPath));

        sb.AppendLine();
        sb.AppendLine("LOGITECH");
        Line("Logi Options+", FindUninstallVersion("Logi Options+") ?? "non détecté");
        var api = @"C:\Program Files\Logi\LogiPluginService\PluginApi.dll";
        Line("Logi Plugin Service", File.Exists(api) ? FileVersionInfo.GetVersionInfo(api).FileVersion : "non détecté");
        Line("Service en cours", Process.GetProcessesByName("LogiPluginService").Length > 0 ? "oui" : "non");
        Line("MX Creative Keypad", FindDevice($"System.Devices.DeviceInstanceId:~~\"{KeypadHardwareId}\"") ? "connecté" : "non détecté");
        Line("MX Creative Dialpad", FindDevice("System.ItemNameDisplay:~~\"Dialpad\"") ? "connecté" : "non détecté");
        Line("Plugin chargé", "phase 3");

        sb.AppendLine();
        sb.AppendLine("AUTOMATISATION");
        Line("URI documenté", "ms-screenclip://capture/image (api-version 1.2)");
        Line("Raccourcis système", "Win+Maj+S, Win+Maj+R, Win+Impr.écran (documentés)");
        Line("UI Automation", "phase 4–6 (AutomationId mesurés en phase 1)");
        return sb.ToString();
    }

    public static List<string> GetMonitors()
    {
        var list = new List<string>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Win32.RECT _, IntPtr _) =>
        {
            var info = new Win32.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32.MONITORINFOEX>() };
            Win32.GetMonitorInfo(h, ref info);
            var scale = Win32.GetDpiForMonitor(h, 0, out var dpi, out _) == 0 ? $"{dpi * 100 / 96} %" : "?";
            list.Add($"{info.szDevice} {info.rcMonitor} échelle {scale}{((info.dwFlags & 1) != 0 ? " (principal)" : string.Empty)}");
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static bool FindDevice(string aqs)
    {
        try
        {
            var found = DeviceInformation.FindAllAsync(aqs, null, DeviceInformationKind.Device).AsTask().GetAwaiter().GetResult();
            return found.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindUninstallVersion(string displayName)
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using var key = root.OpenSubKey(path);
                if (key == null)
                {
                    continue;
                }

                foreach (var sub in key.GetSubKeyNames())
                {
                    using var k = key.OpenSubKey(sub);
                    if (string.Equals(k?.GetValue("DisplayName") as string, displayName, StringComparison.OrdinalIgnoreCase))
                    {
                        return k?.GetValue("DisplayVersion") as string;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Replaces the profile folder by %USERPROFILE% (no user name in exported diagnostics).</summary>
    public static string Mask(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "(non résolu)";
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(profile, StringComparison.OrdinalIgnoreCase) ? "%USERPROFILE%" + path[profile.Length..] : path;
    }

    private static string Exists(string? path) => path != null && Directory.Exists(path) ? "" : "  [n'existe pas encore]";
}
