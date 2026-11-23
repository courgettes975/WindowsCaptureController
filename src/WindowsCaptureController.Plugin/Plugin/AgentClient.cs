namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using WindowsCaptureController.Shared;

    // Client for bin\agent\WccAgent.exe (UI Automation, SendInput, Win32 activation).
    // Started lazily, restarted automatically if it exits (at most once every 10 s after a failure),
    // closed on plugin unload (stdin EOF => clean exit, then kill as a fallback).
    internal sealed class AgentClient : IDisposable
    {
        public const string AgentExeName = "WccAgent.exe";

        private readonly object _sync = new();
        private readonly ConcurrentDictionary<long, TaskCompletionSource<AgentResponse>> _pending = new();
        private Process _process;
        private long _nextId;
        private DateTime _lastStartFailure = DateTime.MinValue;

        private static readonly object SharedGate = new();
        private static AgentClient _shared;
        private static int _refCount;

        private AgentClient(string pluginBinDirectory)
        {
            this.ExePath = Path.Combine(pluginBinDirectory, "agent", AgentExeName);
        }

        /// <summary>
        /// One agent per Logi Plugin Service process. Measured: the service can construct the plugin class
        /// more than once; each instance used to start its own agent. Instances now share this client
        /// (reference counted; the agent is closed when the last instance releases it).
        /// </summary>
        public static AgentClient Acquire(string pluginBinDirectory)
        {
            lock (SharedGate)
            {
                _shared ??= new AgentClient(pluginBinDirectory);
                _refCount++;
                return _shared;
            }
        }

        public static void Release()
        {
            lock (SharedGate)
            {
                if (--_refCount > 0 || _shared == null)
                {
                    return;
                }

                _shared.Dispose();
                _shared = null;
                _refCount = 0;
            }
        }

        public string ExePath { get; }

        public bool IsAvailable => File.Exists(this.ExePath);

        public bool IsDisposed => this._disposed;

        public AgentResponse Call(string cmd, Dictionary<string, string> args = null, int timeoutMs = 4000)
        {
            // A released client (plugin unloaded / reloaded) never restarts an agent: stale timers of an old
            // plugin generation would otherwise keep an orphan agent alive.
            if (this._disposed)
            {
                return new AgentResponse { Ok = false, Status = "Failed", Message = "Plugin déchargé" };
            }

            if (!this.EnsureStarted())
            {
                return new AgentResponse { Ok = false, Status = "Failed", Message = "Agent indisponible" };
            }

            var request = new AgentRequest { Id = Interlocked.Increment(ref this._nextId), Cmd = cmd, Args = args ?? new() };
            var tcs = new TaskCompletionSource<AgentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            this._pending[request.Id] = tcs;
            try
            {
                lock (this._sync)
                {
                    this._process.StandardInput.WriteLine(JsonSerializer.Serialize(request, AgentJson.Options));
                    this._process.StandardInput.Flush();
                }

                if (tcs.Task.Wait(timeoutMs))
                {
                    return tcs.Task.Result;
                }

                PluginLog.Warning($"Agent timeout on {cmd} ({timeoutMs} ms)");
                return new AgentResponse { Ok = false, Status = "Failed", Message = "Délai dépassé" };
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"Agent call {cmd} failed", ex);
                this.Kill();
                return new AgentResponse { Ok = false, Status = "Failed", Message = ex.Message };
            }
            finally
            {
                this._pending.TryRemove(request.Id, out _);
            }
        }

        private bool EnsureStarted()
        {
            lock (this._sync)
            {
                if (this._process is { HasExited: false })
                {
                    return true;
                }

                if (this._disposed || !this.IsAvailable || (DateTime.UtcNow - this._lastStartFailure).TotalSeconds < 10)
                {
                    return false;
                }

                try
                {
                    var exe = ShadowCopy(this.ExePath);
                    var psi = new ProcessStartInfo(exe)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = new System.Text.UTF8Encoding(false),
                        StandardInputEncoding = new System.Text.UTF8Encoding(false),
                        WorkingDirectory = Path.GetDirectoryName(exe)!,
                    };
                    var p = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
                    p.OutputDataReceived += this.OnOutput;
                    p.ErrorDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            PluginLog.Warning("agent stderr: " + e.Data);
                        }
                    };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    this._process = p;
                    PluginLog.Info($"Agent started (pid {p.Id})");
                    return true;
                }
                catch (Exception ex)
                {
                    this._lastStartFailure = DateTime.UtcNow;
                    PluginLog.Error("Cannot start the automation agent", ex);
                    return false;
                }
            }
        }

        // The agent runs from a copy in %LOCALAPPDATA%\WindowsCaptureController\agent-cache\<stamp>, so the
        // installed files (plugin package or dev build output) are never locked: an update or a rebuild can
        // replace them while the plugin is loaded. Stamp = last write time of the agent DLL. Old copies are
        // removed best-effort. Falls back to the original location if the copy fails.
        private static string ShadowCopy(string exePath)
        {
            try
            {
                var srcDir = Path.GetDirectoryName(exePath)!;
                var dll = Path.Combine(srcDir, Path.ChangeExtension(AgentExeName, ".dll"));
                var stamp = File.GetLastWriteTimeUtc(File.Exists(dll) ? dll : exePath).Ticks.ToString();
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsCaptureController", "agent-cache");
                var dst = Path.Combine(root, stamp);
                var ready = Path.Combine(dst, ".complete");
                if (!File.Exists(ready))
                {
                    if (Directory.Exists(dst))
                    {
                        Directory.Delete(dst, true);
                    }

                    foreach (var f in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
                    {
                        var target = Path.Combine(dst, Path.GetRelativePath(srcDir, f));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(f, target, true);
                    }

                    File.WriteAllText(ready, stamp);
                    PluginLog.Info($"Agent copied to agent-cache\\{stamp}");
                }

                foreach (var old in Directory.EnumerateDirectories(root).Where(d => !string.Equals(d, dst, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        Directory.Delete(old, true);
                    }
                    catch
                    {
                        // still used by a running agent: removed next time
                    }
                }

                return Path.Combine(dst, AgentExeName);
            }
            catch (Exception ex)
            {
                PluginLog.Warning("Agent shadow copy failed, running from the plugin folder", ex);
                return exePath;
            }
        }

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            try
            {
                var response = JsonSerializer.Deserialize<AgentResponse>(e.Data, AgentJson.Options);
                if (response != null && this._pending.TryGetValue(response.Id, out var tcs))
                {
                    tcs.TrySetResult(response);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning("agent output ignored: " + ex.GetType().Name);
            }
        }

        private void Kill()
        {
            lock (this._sync)
            {
                try
                {
                    if (this._process is { HasExited: false })
                    {
                        this._process.Kill();
                    }
                }
                catch
                {
                    // already gone
                }

                this._process?.Dispose();
                this._process = null;
            }
        }

        private volatile bool _disposed;

        public void Dispose()
        {
            this._disposed = true;
            try
            {
                lock (this._sync)
                {
                    this._process?.StandardInput.Close(); // EOF => agent exits cleanly
                }

                this._process?.WaitForExit(1000);
            }
            catch
            {
                // ignore
            }

            this.Kill();
        }
    }
}
