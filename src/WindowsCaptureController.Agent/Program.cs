using System.Text;
using System.Text.Json;
using WindowsCaptureController.Logging;
using WindowsCaptureController.Shared;
using WindowsCaptureController.WindowsCapture;

namespace WindowsCaptureController.Agent;

/// <summary>
/// WccAgent.exe — server mode only: JSON requests on stdin, JSON responses on stdout.
/// stdout is the protocol channel: nothing else may be written to it (Log.EchoToConsole stays false).
/// The agent exits when stdin is closed (plugin unloaded) — no orphan process.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);
        Log.EchoToConsole = false;
        Log.MinimumLevel = args.Contains("--debug") ? LogLevel.Debug : LogLevel.Info;
        Log.Info($"WccAgent {typeof(Program).Assembly.GetName().Version} started (pid {Environment.ProcessId})");

        // Validates config.json once at start (warnings go to the log; also sets the log level).
        Configuration.ConfigStore.Load(out var warnings);
        if (args.Contains("--debug"))
        {
            Log.MinimumLevel = LogLevel.Debug;
        }

        // The plugin already debounces per key; the controller keeps its own 400 ms guard as a second safety.
        // config.json is re-read on every action, so edits apply without restarting anything.
        var controller = new WindowsCapture.WindowsCaptureController();

        // Orphan guard: the plugin polls every 1.5 s, so a live agent always receives requests. If the
        // plugin instance disappears without closing our stdin (service reload), the agent exits by itself
        // after IdleTimeout instead of staying alive forever.
        var idleTimeout = TimeSpan.FromSeconds(60);
        var lines = new System.Collections.Concurrent.BlockingCollection<string?>();
        var reader = new Thread(() =>
        {
            try
            {
                string? l;
                while ((l = Console.In.ReadLine()) != null)
                {
                    lines.Add(l);
                }
            }
            catch
            {
                // stdin broken
            }

            lines.Add(null); // EOF
        }) { IsBackground = true, Name = "stdin" };
        reader.Start();

        while (true)
        {
            if (!lines.TryTake(out var line, idleTimeout))
            {
                Log.Info($"WccAgent stopped (no request for {idleTimeout.TotalSeconds:0} s: orphan guard)");
                return 0;
            }

            if (line == null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            AgentRequest? request = null;
            AgentResponse response;
            try
            {
                request = JsonSerializer.Deserialize<AgentRequest>(line, AgentJson.Options);
                response = request == null ? Error(0, "requête vide") : Handle(controller, request);
            }
            catch (Exception ex)
            {
                Log.Error("Agent request failed", ex);
                response = Error(request?.Id ?? 0, $"Erreur agent ({ex.GetType().Name})");
            }

            Console.Out.WriteLine(JsonSerializer.Serialize(response, AgentJson.Options));
            Console.Out.Flush();
        }

        Log.Info("WccAgent stopped (stdin closed)");
        return 0;
    }

    private static AgentResponse Handle(WindowsCapture.WindowsCaptureController c, AgentRequest r)
    {
        string Arg(string k, string d) => r.Args.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : d;

        switch (r.Cmd)
        {
            case AgentCommands.Ping:
                return new AgentResponse { Id = r.Id, Ok = true, Status = "Success", Message = "pong" };
            case AgentCommands.State:
                var state = c.GetApplicationState();
                return new AgentResponse
                {
                    Id = r.Id, Ok = true, Status = "Success", Message = state.ToString(),
                    Data = { ["state"] = state.ToString() },
                };
        }

        if (r.Cmd == AgentCommands.VideoState)
        {
            var snap = Automation.RecordingAutomation.Snapshot(detectPause: false);
            return new AgentResponse
            {
                Id = r.Id, Ok = true, Status = "Success", Message = snap.State.ToString(),
                Data =
                {
                    ["state"] = snap.State.ToString(), ["enabled"] = snap.ControlsEnabled.ToString(),
                    ["timer"] = snap.Timer, ["mic"] = snap.Microphone.ToString(), ["audio"] = snap.SystemAudio.ToString(),
                },
            };
        }

        if (r.Cmd == AgentCommands.EditState)
        {
            var e = Automation.EditorAutomation.Snapshot();
            return new AgentResponse
            {
                Id = r.Id, Ok = true, Status = "Success", Message = e.Open ? e.Tool.ToString() : "closed",
                Data = { ["open"] = e.Open.ToString(), ["tool"] = e.Tool.ToString(), ["undo"] = e.CanUndo.ToString(), ["redo"] = e.CanRedo.ToString() },
            };
        }

        int? IntArg(string k) => r.Args.TryGetValue(k, out var v) && int.TryParse(v, out var i) ? i : null;

        if (r.Cmd == AgentCommands.ConfigGet)
        {
            var cfg = Configuration.ConfigStore.Load();
            return new AgentResponse
            {
                Id = r.Id, Ok = true, Status = "Success", Message = "config",
                Data =
                {
                    ["defaultDialMode"] = cfg.DefaultDialMode, ["dialDirection"] = cfg.DialDirection,
                    ["dialSensitivity"] = cfg.DialSensitivity.ToString(), ["captureDelaySeconds"] = cfg.CaptureDelaySeconds.ToString(),
                },
            };
        }

        if (r.Cmd == AgentCommands.HistoryList)
        {
            var items = c.GetHistory(Math.Clamp(IntArg("max") ?? 30, 1, 100));
            var resp = new AgentResponse { Id = r.Id, Ok = true, Status = "Success", Message = $"{items.Count} élément(s)" };
            resp.Data["count"] = items.Count.ToString();
            for (var i = 0; i < items.Count; i++)
            {
                resp.Data[i.ToString()] = $"{items[i].Kind}|{items[i].Time:yyyy-MM-dd HH:mm}|{items[i].Name}";
            }

            return resp;
        }

        ActionResult result = r.Cmd switch
        {
            AgentCommands.Launch => c.Launch(),
            AgentCommands.Front => c.BringToFront(),
            AgentCommands.NewCapture => c.StartNewCapture(),
            AgentCommands.Rectangle => c.StartRectangularCapture(),
            AgentCommands.Window => c.StartWindowCapture(),
            AgentCommands.Freeform => c.StartFreeformCapture(),
            AgentCommands.Fullscreen => c.StartFullscreenCapture(),
            AgentCommands.DefaultCapture => c.StartDefaultCapture(),
            AgentCommands.MonitorCapture => c.CaptureMonitor(Arg("mode", "active-monitor"), Arg("output", "file"),
                r.Args.TryGetValue("folder", out var f) && !string.IsNullOrWhiteSpace(f) ? f : null),
            AgentCommands.OpenLastCapture => c.OpenLastCapture(),
            AgentCommands.CapturesFolder => c.OpenCapturesFolder(),
            AgentCommands.VideosFolder => c.OpenVideosFolder(),
            AgentCommands.VideoMode => c.OpenVideoMode(),
            AgentCommands.VideoStart => c.StartRecording(),
            AgentCommands.VideoStop => c.StopRecording(),
            AgentCommands.VideoPause => c.TogglePauseRecording(),
            AgentCommands.VideoMic => c.ToggleRecordingMicrophone(),
            AgentCommands.VideoSaveAs => c.SaveVideoAs(),
            AgentCommands.HistoryOpen => c.OpenHistoryItem(Arg("name", "")),
            AgentCommands.EditPen => c.SelectPen(),
            AgentCommands.EditHighlighter => c.SelectHighlighter(),
            AgentCommands.EditEraser => c.SelectEraser(),
            AgentCommands.EditUndo => c.Undo(),
            AgentCommands.EditRedo => c.Redo(),
            AgentCommands.EditCopy => c.Copy(),
            AgentCommands.EditSaveAs => c.SaveAs(),
            AgentCommands.EditShare => c.Share(),
            AgentCommands.EditRuler => c.SelectRuler(),
            AgentCommands.EditClearAll => c.ClearAllAnnotations(Arg("confirmed", "false") == "true"),
            AgentCommands.EditColor => c.SetStrokeColor(IntArg("index"), IntArg("delta") ?? 0),
            AgentCommands.EditWidth => c.SetStrokeWidth(IntArg("value"), IntArg("delta") ?? 0, Arg("reset", "false") == "true"),
            AgentCommands.ZoomIn => c.ZoomIn(),
            AgentCommands.ZoomOut => c.ZoomOut(),
            AgentCommands.ZoomReset => c.ResetZoom(),
            AgentCommands.ZoomFit => c.FitToWindow(),
            AgentCommands.VideoDiscard => c.DiscardRecording(Arg("confirmed", "false") == "true"),
            AgentCommands.OpenLastVideo => c.OpenLastVideo(),
            _ => ActionResult.Unavailable($"Commande inconnue : {r.Cmd}"),
        };

        return new AgentResponse
        {
            Id = r.Id,
            Ok = result.IsOk,
            Status = result.Status.ToString(),
            Method = result.Method.ToString(),
            Message = result.Message,
            Hint = result.Hint,
        };
    }

    private static AgentResponse Error(long id, string message) =>
        new() { Id = id, Ok = false, Status = "Failed", Message = message };
}
