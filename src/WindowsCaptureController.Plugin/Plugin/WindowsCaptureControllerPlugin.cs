namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    // Plugin entry point loaded by the Logi Plugin Service (Logi Options+).
    // Universal plugin: its keys work whatever application is in the foreground.
    public class WindowsCaptureControllerPlugin : Plugin
    {
        public const string ShortName = "WindowsCaptureController";

        public override bool UsesApplicationApiOnly => true;

        public override bool HasNoApplication => true;

        public WindowsCaptureControllerPlugin()
        {
            PluginLog.Init(this.Log);
        }

        /// <summary>
        /// Shared agent client, acquired in Load and released in Unload (NOT in the constructor: the service
        /// may construct the plugin class more than once without ever loading the extra instances).
        /// Null before Load / after Unload: keys then show "Erreur" instead of starting an agent.
        /// </summary>
        internal AgentClient Agent { get; private set; }

        /// <summary>Dial mode / values shared by the Dialpad adjustment and the "Mode molette" key.</summary>
        internal DialState Dial { get; } = new DialState();

        /// <summary>Reads dial settings from config.json through the agent (validated there).</summary>
        internal void RefreshDialSettings(bool applyDefaultMode = false)
        {
            try
            {
                var r = this.Agent?.Call(WindowsCaptureController.Shared.AgentCommands.ConfigGet, timeoutMs: 3000);
                if (r == null || !r.Ok)
                {
                    return;
                }

                this.Dial.Sensitivity = int.TryParse(r.Data.GetValueOrDefault("dialSensitivity", "1"), out var s) ? s : 1;
                this.Dial.Inverted = r.Data.GetValueOrDefault("dialDirection") == "inverted";
                if (applyDefaultMode)
                {
                    this.Dial.Mode = r.Data.GetValueOrDefault("defaultDialMode", "zoom") switch
                    {
                        "thickness" => "thickness", "color" => "color", "history" => "history", "delay" => "delay", _ => "zoom",
                    };
                    var delay = int.TryParse(r.Data.GetValueOrDefault("captureDelaySeconds", "0"), out var d) ? d : 0;
                    this.Dial.DelayIndex = Math.Max(0, Array.IndexOf(DialState.Delays, delay));
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning("dial settings not refreshed", ex);
            }
        }

        public override void Load()
        {
            try
            {
                this.Agent = AgentClient.Acquire(FindPluginBinDirectory());
                PluginLog.Info($"Windows Capture Controller {typeof(WindowsCaptureControllerPlugin).Assembly.GetName().Version} loaded");
                var (svgOk, svgTotal) = SvgIcons.SelfTest();
                PluginLog.Info($"SVG icons rendered by the SDK: {svgOk}/{svgTotal} (others use the drawn fallback)");
                if (!this.Agent.IsAvailable)
                {
                    PluginLog.Warning("Agent missing: " + this.Agent.ExePath);
                    this.OnPluginStatusChanged(Loupedeck.PluginStatus.Error,
                        "Agent d'automatisation (WccAgent.exe) introuvable : réinstallez le plugin.");
                    return;
                }

                var ping = this.Agent.Call(WindowsCaptureController.Shared.AgentCommands.Ping, timeoutMs: 8000);
                if (ping.Ok)
                {
                    PluginLog.Info("Agent ping OK");
                    this.OnPluginStatusChanged(Loupedeck.PluginStatus.Normal, null);
                    this.RefreshDialSettings(applyDefaultMode: true);
                }
                else
                {
                    PluginLog.Warning("Agent ping failed: " + ping.Message);
                    this.OnPluginStatusChanged(Loupedeck.PluginStatus.Warning,
                        "L'agent d'automatisation ne répond pas. Voir %LOCALAPPDATA%\\WindowsCaptureController\\logs");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error("Load failed", ex);
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Error,
                    "Erreur au chargement : voir %LOCALAPPDATA%\\WindowsCaptureController\\logs");
            }
        }

        public override void Unload()
        {
            if (this.Agent != null)
            {
                this.Agent = null;
                AgentClient.Release();
            }
        }

        // The Logi Plugin Service does not always expose the real plugin folder through Assembly.Location
        // (observed with MXTeamViewer: it can return the service folder). Probe, in order: the assembly
        // folder, the developer .link target, then the installed package folder.
        private static string FindPluginBinDirectory()
        {
            var candidates = new List<string>();
            var asm = typeof(WindowsCaptureControllerPlugin).Assembly.Location;
            if (!string.IsNullOrEmpty(asm))
            {
                candidates.Add(Path.GetDirectoryName(asm));
            }

            var plugins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logi", "LogiPluginService", "Plugins");
            try
            {
                var link = Path.Combine(plugins, ShortName + ".link");
                if (File.Exists(link))
                {
                    candidates.Add(Path.Combine(File.ReadAllText(link).Trim(), "bin"));
                }
            }
            catch
            {
                // unreadable link: ignore
            }

            candidates.Add(Path.Combine(plugins, ShortName, "bin"));
            foreach (var dir in candidates)
            {
                if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "agent", AgentClient.AgentExeName)))
                {
                    return dir;
                }
            }

            return candidates.Count > 0 ? candidates[0] : AppContext.BaseDirectory;
        }
    }

    // Required by the Logi Plugin Service loader even for universal plugins (it refuses a plugin
    // without a ClientApplication subclass). Empty process name: not bound to an application.
    public class WindowsCaptureApplication : ClientApplication
    {
        protected override string GetProcessName() => "";

        protected override string GetBundleName() => "";

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}
