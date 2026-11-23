using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsCaptureController.Logging;

namespace WindowsCaptureController.Configuration;

/// <summary>
/// Loads and validates config.json. Never throws: a missing file is created with defaults, an unreadable
/// file falls back to defaults, an invalid value falls back to its default with a WARNING in the log.
/// </summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsCaptureController", "config.json");

    /// <summary>Loads (and creates if missing) the configuration. <paramref name="warnings"/> lists every correction.</summary>
    public static WccConfig Load(out List<string> warnings, string? path = null)
    {
        path ??= DefaultPath;
        warnings = new List<string>();
        try
        {
            if (!File.Exists(path))
            {
                var fresh = new WccConfig();
                Save(fresh, path);
                Log.Info("config.json created with default values");
                return fresh;
            }

            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject;
            if (node == null)
            {
                warnings.Add("config.json n'est pas un objet JSON : valeurs par défaut utilisées.");
                return Report(new WccConfig(), warnings);
            }

            return Report(FromJson(node, warnings), warnings);
        }
        catch (Exception ex)
        {
            warnings.Add($"config.json illisible ({ex.GetType().Name}) : valeurs par défaut utilisées.");
            return Report(new WccConfig(), warnings);
        }
    }

    public static WccConfig Load(string? path = null) => Load(out _, path);

    public static void Save(WccConfig config, string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(config, WriteOptions));
        }
        catch (Exception ex)
        {
            Log.Warning($"config.json not saved ({ex.GetType().Name})");
        }
    }

    /// <summary>Field-by-field validation: one bad value never invalidates the others.</summary>
    internal static WccConfig FromJson(JsonObject o, List<string> w)
    {
        var d = new WccConfig();
        var c = new WccConfig
        {
            CaptureFolder = Folder(o, "captureFolder", d.CaptureFolder, w),
            VideoFolder = Folder(o, "videoFolder", d.VideoFolder, w),
            DefaultCaptureMode = OneOf(o, "defaultCaptureMode", WccConfig.CaptureModes, d.DefaultCaptureMode, w),
            DefaultDialMode = OneOf(o, "defaultDialMode", WccConfig.DialModes, d.DefaultDialMode, w),
            DialDirection = OneOf(o, "dialDirection", WccConfig.DialDirections, d.DialDirection, w),
            DialSensitivity = IntRange(o, "dialSensitivity", 1, 10, d.DialSensitivity, w),
            AutoSave = Bool(o, "autoSave", d.AutoSave, w),
            AutoSaveImageFormat = OneOf(o, "autoSaveImageFormat", WccConfig.ImageFormats, d.AutoSaveImageFormat, w),
            CaptureDelaySeconds = IntOneOf(o, "captureDelaySeconds", WccConfig.Delays, d.CaptureDelaySeconds, w),
            MultiMonitorMode = OneOf(o, "multiMonitorMode", WccConfig.MonitorModes, d.MultiMonitorMode, w),
            FullscreenOutput = OneOf(o, "fullscreenOutput", WccConfig.Outputs, d.FullscreenOutput, w),
            ClipboardFeedbackDurationMs = IntRange(o, "clipboardFeedbackDurationMs", 500, 5000, d.ClipboardFeedbackDurationMs, w),
            EnableContextProfiles = Bool(o, "enableContextProfiles", d.EnableContextProfiles, w),
            EnableOcrActions = Bool(o, "enableOcrActions", d.EnableOcrActions, w),
            EnableSensitiveDataActions = Bool(o, "enableSensitiveDataActions", d.EnableSensitiveDataActions, w),
            MicrophoneControlMode = OneOf(o, "microphoneControlMode", WccConfig.MicrophoneModes, d.MicrophoneControlMode, w),
            UiLanguage = OneOf(o, "uiLanguage", WccConfig.Languages, d.UiLanguage, w),
            LoggingLevel = OneOf(o, "loggingLevel", WccConfig.LogLevels, d.LoggingLevel, w),
        };

        var known = typeof(WccConfig).GetProperties()
            .Select(p => p.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>().FirstOrDefault()?.Name)
            .Where(n => n != null).ToHashSet();
        foreach (var key in o.Select(kv => kv.Key).Where(k => !known.Contains(k)))
        {
            w.Add($"Clé inconnue ignorée : « {key} ».");
        }

        return c;
    }

    /// <summary>Resolves "AUTO"/environment variables of a folder setting; null when AUTO.</summary>
    public static string? ResolveFolder(string value) =>
        string.Equals(value, "AUTO", StringComparison.OrdinalIgnoreCase) ? null : Environment.ExpandEnvironmentVariables(value);

    private static WccConfig Report(WccConfig c, List<string> warnings)
    {
        foreach (var w in warnings)
        {
            Log.Warning("config: " + w);
        }

        Log.MinimumLevel = Log.ParseLevel(c.LoggingLevel);
        return c;
    }

    private static string? Str(JsonObject o, string key, List<string> w)
    {
        if (!o.TryGetPropertyValue(key, out var n) || n == null)
        {
            return null;
        }

        if (n is JsonValue v && v.TryGetValue<string>(out var s))
        {
            return s;
        }

        w.Add($"« {key} » doit être une chaîne.");
        return null;
    }

    private static string OneOf(JsonObject o, string key, string[] allowed, string def, List<string> w)
    {
        var s = Str(o, key, w);
        if (s == null)
        {
            return def;
        }

        var match = allowed.FirstOrDefault(a => string.Equals(a, s.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            return match;
        }

        w.Add($"« {key} » = « {s} » invalide (attendu : {string.Join(", ", allowed)}) ; « {def} » utilisé.");
        return def;
    }

    private static string Folder(JsonObject o, string key, string def, List<string> w)
    {
        var s = Str(o, key, w);
        if (s == null || string.Equals(s, "AUTO", StringComparison.OrdinalIgnoreCase))
        {
            return def;
        }

        var expanded = Environment.ExpandEnvironmentVariables(s);
        if (!Path.IsPathFullyQualified(expanded) || expanded.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            w.Add($"« {key} » = « {s} » n'est pas un chemin absolu valide ; AUTO utilisé.");
            return def;
        }

        return s;
    }

    private static bool Bool(JsonObject o, string key, bool def, List<string> w)
    {
        if (!o.TryGetPropertyValue(key, out var n) || n == null)
        {
            return def;
        }

        if (n is JsonValue v && v.TryGetValue<bool>(out var b))
        {
            return b;
        }

        w.Add($"« {key} » doit être true ou false ; {def.ToString().ToLowerInvariant()} utilisé.");
        return def;
    }

    private static int? Int(JsonObject o, string key, List<string> w)
    {
        if (!o.TryGetPropertyValue(key, out var n) || n == null)
        {
            return null;
        }

        if (n is JsonValue v && v.TryGetValue<int>(out var i))
        {
            return i;
        }

        w.Add($"« {key} » doit être un entier.");
        return null;
    }

    private static int IntOneOf(JsonObject o, string key, int[] allowed, int def, List<string> w)
    {
        var i = Int(o, key, w);
        if (i == null)
        {
            return def;
        }

        if (allowed.Contains(i.Value))
        {
            return i.Value;
        }

        w.Add($"« {key} » = {i} invalide (attendu : {string.Join(", ", allowed)}) ; {def} utilisé.");
        return def;
    }

    private static int IntRange(JsonObject o, string key, int min, int max, int def, List<string> w)
    {
        var i = Int(o, key, w);
        if (i == null)
        {
            return def;
        }

        if (i >= min && i <= max)
        {
            return i.Value;
        }

        w.Add($"« {key} » = {i} hors limites ({min}–{max}) ; {def} utilisé.");
        return def;
    }
}
