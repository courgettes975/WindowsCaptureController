using System.Text;
using WindowsCaptureController.Diagnostics;
using WindowsCaptureController.Logging;
using WindowsCaptureController.WindowsCapture;

namespace WindowsCaptureController.Cli;

/// <summary>
/// Diagnostic / test CLI.
/// Safety: no silent recording is possible from here; "test fullscreen" takes a REAL screenshot of the
/// user's screens and therefore requires --confirm; capture tests close the overlay with --cancel
/// (Escape is sent only if the overlay owns the foreground).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Log.EchoToConsole = !args.Contains("--quiet");
        Log.MinimumLevel = args.Contains("--debug") ? LogLevel.Debug : LogLevel.Info;

        var a = args.Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToArray();
        var cancel = args.Contains("--cancel");
        var ctl = new WindowsCapture.WindowsCaptureController();

        try
        {
            switch (a.FirstOrDefault()?.ToLowerInvariant())
            {
                case "diagnose":
                    Console.WriteLine(SystemDiagnostics.BuildReport());
                    return 0;

                case "list-actions":
                    ListActions();
                    return 0;

                case "list-windows":
                    foreach (var w in SnippingToolLocator.GetWindows())
                    {
                        Console.WriteLine($"hwnd=0x{w.Handle:X} pid={w.ProcessId} class={w.ClassName} minimized={w.Minimized} rect={w.Bounds}");
                    }

                    return 0;

                case "folders":
                    return Report(ctl.OpenCapturesFolder());

                case "test":
                    return RunTest(ctl, a.Skip(1).ToArray(), cancel, args.Contains("--confirm"));

                default:
                    Usage();
                    return a.Length == 0 ? 0 : 2;
            }
        }
        catch (Exception ex)
        {
            Log.Error("CLI failed", ex);
            return 1;
        }
    }

    private static int RunTest(IWindowsCaptureController ctl, string[] a, bool cancel, bool confirm)
    {
        switch (a.FirstOrDefault()?.ToLowerInvariant())
        {
            case "launch":
                return Report(ctl.Launch());

            case "front":
                return Report(ctl.BringToFront());

            case "state":
                Console.WriteLine($"État : {ctl.GetApplicationState()}");
                return 0;

            case "capture":
            case "newcapture":
            {
                var mode = (a.ElementAtOrDefault(1) ?? "rectangle").ToLowerInvariant();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = a[0].Equals("newcapture", StringComparison.OrdinalIgnoreCase)
                    ? ctl.StartNewCapture()
                    : mode switch
                    {
                        "window" => ctl.StartWindowCapture(),
                        "freeform" => ctl.StartFreeformCapture(),
                        _ => ctl.StartRectangularCapture(),
                    };
                var code = Report(result);
                Console.WriteLine($"Durée de l'action : {sw.ElapsedMilliseconds} ms");

                var overlay = Automation.CaptureOverlayAutomation.FindToolbarWindow();
                if (overlay == null)
                {
                    Console.WriteLine("Overlay NON détecté (UI Automation).");
                    return 3;
                }

                var idx = Automation.CaptureOverlayAutomation.GetSelectedModeIndex(overlay);
                Console.WriteLine($"Overlay ouvert ; mode sélectionné (index) = {idx}  [0=rectangle 1=fenêtre 2=plein écran 3=forme libre]");
                if (cancel)
                {
                    Thread.Sleep(400);
                    var closed = Automation.CaptureOverlayAutomation.Close(overlay);
                    Thread.Sleep(800);
                    Console.WriteLine($"Fermeture via CloseButton : {closed} ; overlay encore ouvert : {Automation.CaptureOverlayAutomation.IsOpen()}");
                }

                return code;
            }

            case "uri-results":
            {
                // Experimental: documented LaunchUriForResultsAsync path for unpackaged callers.
                var mode = (a.ElementAtOrDefault(1) ?? "rectangle").ToLowerInvariant() switch
                {
                    "window" => CaptureMode.Window,
                    "freeform" => CaptureMode.Freeform,
                    _ => CaptureMode.Rectangle,
                };
                var uri = ScreenClipUri.Image(mode);
                Console.WriteLine($"URI : {uri}");
                var before = WindowActivator.ForegroundWindow;

                // Owner window: a real, transparent top-level window on an STA thread with a message loop.
                var tcs = new TaskCompletionSource<ScreenClipResponse>();
                var ui = new Thread(() =>
                {
                    using var form = new System.Windows.Forms.Form
                    {
                        ShowInTaskbar = false,
                        Opacity = 0,
                        FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
                        Size = new System.Drawing.Size(1, 1),
                        StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                        Location = new System.Drawing.Point(0, 0),
                    };
                    form.Shown += async (_, _) =>
                    {
                        try
                        {
                            tcs.TrySetResult(await ScreenClipLauncher.LaunchImageForResultsAsync(uri, form.Handle));
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                        finally
                        {
                            form.Close();
                        }
                    };
                    System.Windows.Forms.Application.Run(form);
                });
                ui.SetApartmentState(ApartmentState.STA);
                ui.IsBackground = true;
                ui.Start();
                var task = tcs.Task;
                var overlay = OverlayWatcher.WaitForOverlay(before, 4000);
                Console.WriteLine(overlay != null
                    ? $"Overlay détecté : process={overlay.ProcessName} class={overlay.ClassName} rect={overlay.Bounds} en {overlay.ElapsedMs} ms"
                    : "Overlay NON détecté.");
                if (cancel && overlay != null)
                {
                    Thread.Sleep(600);
                    OverlayWatcher.CancelOverlay();
                }

                if (task.Wait(TimeSpan.FromSeconds(cancel ? 10 : 60)))
                {
                    var r = task.Result;
                    Console.WriteLine($"Réponse : status={r.LaunchStatus} code={r.Code} reason={r.Reason} jeton={(r.HasFileToken ? "oui" : "non")}");
                }
                else
                {
                    Console.WriteLine("Pas de réponse dans le délai.");
                }

                return overlay != null ? 0 : 3;
            }

            case "fullscreen":
                if (!confirm)
                {
                    Console.WriteLine("Ce test prend une VRAIE capture de tous vos écrans (Win+Impr.écran) et l'enregistre dans le dossier Captures d'écran.");
                    Console.WriteLine("Relancez avec --confirm pour l'exécuter.");
                    return 4;
                }

                return Report(ctl.StartFullscreenCapture());

            case "debounce":
            {
                var d = new ActionDebouncer(400);
                var first = d.TryEnter("x");
                var second = d.TryEnter("x");
                Thread.Sleep(450);
                var third = d.TryEnter("x");
                Console.WriteLine($"1er={first} 2e(immédiat)={second} 3e(+450 ms)={third}");
                return first && !second && third ? 0 : 1;
            }

            default:
                Usage();
                return 2;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    private static int Report(ActionResult r)
    {
        Console.WriteLine($"=> {r}");
        return r.IsOk ? 0 : 1;
    }

    private static void ListActions()
    {
        var ctl = typeof(IWindowsCaptureController);
        foreach (var m in ctl.GetMethods().OrderBy(m => m.Name))
        {
            Console.WriteLine($"  {m.Name}");
        }
    }

    private static void Usage()
    {
        Console.WriteLine("""
            Windows Capture Controller — CLI de diagnostic (phase 2)

              diagnose                      Rapport système / Outil Capture / Logitech (lecture seule)
              list-actions                  Actions de l'interface IWindowsCaptureController
              list-windows                  Fenêtres de l'Outil Capture
              folders                       Ouvre le dossier Captures d'écran
              test launch                   Lance ou affiche l'Outil Capture (une seule instance)
              test front                    Met l'Outil Capture au premier plan
              test state                    État détecté
              test capture <rectangle|window|freeform> [--cancel]
                                            Ouvre la sélection par URI documenté ; --cancel ferme l'overlay (Échap)
              test newcapture [--cancel]    Ouvre la sélection par Win+Maj+S
              test fullscreen --confirm     VRAIE capture plein écran (Win+Impr.écran)
              test debounce                 Vérifie l'anti-rebond

            Options : --debug  --quiet
            Journaux : %LOCALAPPDATA%\WindowsCaptureController\logs\wcc.log
            """);
    }
}
