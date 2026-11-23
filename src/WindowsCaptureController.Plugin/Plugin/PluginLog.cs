namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System;
    using System.IO;
    using System.Text;

    // Plugin-side log: %LOCALAPPDATA%\WindowsCaptureController\logs\plugin.log (rotated at 5 MB, 5 files),
    // also forwarded to the Logi Plugin Service log. Same privacy rule as the agent log:
    // action names, statuses and errors only — never clipboard, image, OCR text or window content.
    internal static class PluginLog
    {
        private const long MaxBytes = 5 * 1024 * 1024;
        private const int MaxFiles = 5;
        private static readonly object Sync = new();
        private static PluginLogFile _service;
        private static string _file;

        public static void Init(PluginLogFile service)
        {
            _service = service;
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsCaptureController", "logs");
            _file = Path.Combine(dir, "plugin.log");
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch
            {
                // logging must never break the plugin
            }
        }

        public static void Info(string message) => Write("INFO", message, null);

        public static void Warning(string message, Exception ex = null) => Write("WARNING", message, ex);

        public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception ex)
        {
            var text = ex == null ? message : $"{message} ({ex.GetType().Name}: {ex.Message})";
            try
            {
                switch (level)
                {
                    case "ERROR": _service?.Error(text); break;
                    case "WARNING": _service?.Warning(text); break;
                    default: _service?.Info(text); break;
                }
            }
            catch
            {
                // ignore
            }

            if (_file == null)
            {
                return;
            }

            try
            {
                lock (Sync)
                {
                    Rotate();
                    File.AppendAllText(_file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {level,-7} {text}{Environment.NewLine}", Encoding.UTF8);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static void Rotate()
        {
            var fi = new FileInfo(_file);
            if (!fi.Exists || fi.Length < MaxBytes)
            {
                return;
            }

            var dir = fi.DirectoryName!;
            var oldest = Path.Combine(dir, $"plugin.{MaxFiles - 1}.log");
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (var i = MaxFiles - 2; i >= 1; i--)
            {
                var src = Path.Combine(dir, $"plugin.{i}.log");
                if (File.Exists(src))
                {
                    File.Move(src, Path.Combine(dir, $"plugin.{i + 1}.log"));
                }
            }

            File.Move(_file, Path.Combine(dir, "plugin.1.log"));
        }
    }
}
