using System.Runtime.InteropServices;
using Windows.Foundation.Collections;
using Windows.System;
using WindowsCaptureController.Logging;

namespace WindowsCaptureController.WindowsCapture;

/// <summary>Outcome of a documented "launch for results" request.</summary>
public sealed record ScreenClipResponse(LaunchUriStatus LaunchStatus, int? Code, string? Reason, bool HasFileToken);

/// <summary>
/// Documented path for UNPACKAGED callers (Microsoft Learn, "Receive an image result directly"):
/// Launcher.LaunchUriForResultsAsync + LauncherOptions.TargetApplicationPackageFamilyName, associated with an owner HWND.
/// The returned file-access-token cannot be redeemed without package identity: we only read code/reason,
/// the image itself reaches the user through the clipboard / Snipping Tool auto-save.
/// Image requests only (video returns 501 per the documentation).
/// </summary>
public static class ScreenClipLauncher
{
    [ComImport, Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithWindow
    {
        void Initialize(IntPtr hwnd);
    }

    public static async Task<ScreenClipResponse> LaunchImageForResultsAsync(string uri, IntPtr ownerHwnd, CancellationToken ct = default)
    {
        var options = new LauncherOptions { TargetApplicationPackageFamilyName = SnippingToolLocator.PackageFamilyName };
        if (ownerHwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(options, ownerHwnd);
        }

        var result = await Launcher.LaunchUriForResultsAsync(new Uri(uri), options).AsTask(ct).ConfigureAwait(false);
        int? code = null;
        string? reason = null;
        var hasToken = false;
        if (result.Status == LaunchUriStatus.Success && result.Result is ValueSet values)
        {
            if (values.TryGetValue("code", out var c) && c is int i)
            {
                code = i;
            }

            reason = values.TryGetValue("reason", out var r) ? r as string : null;
            hasToken = values.ContainsKey("file-access-token"); // value never logged
        }

        Log.Info($"LaunchUriForResults: status={result.Status} code={code?.ToString() ?? "-"} reason={reason ?? "-"} token={(hasToken ? "yes" : "no")}");
        return new ScreenClipResponse(result.Status, code, reason, hasToken);
    }
}
