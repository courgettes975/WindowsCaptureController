using System.Text;

namespace WindowsCaptureController.Logging;

public enum LogLevel { Trace = 0, Debug = 1, Info = 2, Warning = 3, Error = 4 }

/// <summary>
/// Small thread-safe file logger.
/// Location: %LOCALAPPDATA%\WindowsCaptureController\logs\wcc.log, rotated to wcc.1.log ... wcc.4.log
/// (5 files max, 5 MB each).
/// PRIVACY RULE: callers must never pass clipboard content, OCR text, image/video data or
/// window contents. Only action names, methods, states, process names and error types.
/// </summary>
public static class Log
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxFiles = 5;

    private static readonly object Gate = new();
    private static string? _directory;

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>Also write each line to the console (CLI use).</summary>
    public static bool EchoToConsole { get; set; }

    public static string Directory
    {
        get => _directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsCaptureController", "logs");
        set => _directory = value;
    }

    public static string CurrentFile => Path.Combine(Directory, "wcc.log");

    public static void Trace(string message) => Write(LogLevel.Trace, message);
    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warning(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, ex == null ? message : $"{message} ({ex.GetType().Name}: {ex.Message})");

    public static LogLevel ParseLevel(string? value, LogLevel fallback = LogLevel.Info) =>
        Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level) ? level : fallback;

    public static void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {LevelTag(level),-7} {message}";
        if (EchoToConsole)
        {
            Console.WriteLine(line);
        }

        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                RotateIfNeeded();
                File.AppendAllText(CurrentFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the caller (and never the Logi Plugin Service).
        }
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Warning => "WARNING",
        _ => level.ToString().ToUpperInvariant(),
    };

    internal static void RotateIfNeeded()
    {
        var current = new FileInfo(CurrentFile);
        if (!current.Exists || current.Length < MaxFileBytes)
        {
            return;
        }

        // wcc.4.log is dropped, wcc.3 -> wcc.4, ..., wcc.log -> wcc.1.log
        var oldest = Path.Combine(Directory, $"wcc.{MaxFiles - 1}.log");
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = MaxFiles - 2; i >= 1; i--)
        {
            var src = Path.Combine(Directory, $"wcc.{i}.log");
            if (File.Exists(src))
            {
                File.Move(src, Path.Combine(Directory, $"wcc.{i + 1}.log"));
            }
        }

        File.Move(CurrentFile, Path.Combine(Directory, "wcc.1.log"));
    }
}
