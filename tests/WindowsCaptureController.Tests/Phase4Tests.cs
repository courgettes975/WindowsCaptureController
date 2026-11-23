using System.Drawing;
using System.Text.Json.Nodes;
using WindowsCaptureController.Capture;
using WindowsCaptureController.Configuration;
using WindowsCaptureController.Storage;

namespace WindowsCaptureController.Tests;

public class ConfigValidationTests
{
    private static WccConfig Parse(string json, out List<string> warnings)
    {
        warnings = new List<string>();
        return ConfigStore.FromJson((JsonObject)JsonNode.Parse(json)!, warnings);
    }

    [Fact]
    public void Valid_file_is_read_without_warnings()
    {
        var c = Parse("""
            { "captureFolder": "%USERPROFILE%\\Pictures\\Captures", "defaultCaptureMode": "window",
              "captureDelaySeconds": 5, "multiMonitorMode": "pointer-monitor", "fullscreenOutput": "both",
              "autoSave": true, "loggingLevel": "debug" }
            """, out var w);

        Assert.Empty(w);
        Assert.Equal("window", c.DefaultCaptureMode);
        Assert.Equal(5, c.CaptureDelaySeconds);
        Assert.Equal("pointer-monitor", c.MultiMonitorMode);
        Assert.True(c.AutoSave);
    }

    [Fact]
    public void Each_invalid_value_falls_back_to_its_default_independently()
    {
        var c = Parse("""
            { "defaultCaptureMode": "triangle", "captureDelaySeconds": 7, "multiMonitorMode": "primary",
              "clipboardFeedbackDurationMs": 99999, "autoSave": "yes", "captureFolder": "relative\\path" }
            """, out var w);

        Assert.Equal("rectangle", c.DefaultCaptureMode);
        Assert.Equal(0, c.CaptureDelaySeconds);
        Assert.Equal("primary", c.MultiMonitorMode);          // the valid value next to bad ones is kept
        Assert.Equal(1500, c.ClipboardFeedbackDurationMs);
        Assert.False(c.AutoSave);
        Assert.Equal("AUTO", c.CaptureFolder);
        Assert.Equal(5, w.Count);
    }

    [Fact]
    public void Values_are_case_insensitive_and_unknown_keys_are_reported()
    {
        var c = Parse("""{ "defaultCaptureMode": "FreeForm", "colour": "red" }""", out var w);

        Assert.Equal("freeform", c.DefaultCaptureMode);
        Assert.Single(w);
        Assert.Contains("colour", w[0]);
    }

    [Fact]
    public void Missing_file_is_created_with_defaults_and_broken_file_never_throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wcc-cfg-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "config.json");
        try
        {
            var created = ConfigStore.Load(out var w1, path);
            Assert.True(File.Exists(path));
            Assert.Empty(w1);
            Assert.Equal("all-screens", created.MultiMonitorMode);

            File.WriteAllText(path, "{ this is not json");
            var fallback = ConfigStore.Load(out var w2, path);
            Assert.Single(w2);
            Assert.Equal("rectangle", fallback.DefaultCaptureMode);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Auto_folder_resolves_to_null_and_env_vars_expand()
    {
        Assert.Null(ConfigStore.ResolveFolder("auto"));
        Assert.DoesNotContain("%", ConfigStore.ResolveFolder("%TEMP%\\x"));
    }
}

public class MonitorSelectionTests
{
    // Same layout as the test machine: DISPLAY1 primary at 0,0, DISPLAY2 at 1920,0, DISPLAY3 at 3840,0.
    private static readonly List<MonitorInfo> Three = new()
    {
        new("\\\\.\\DISPLAY2", new Rectangle(1920, 0, 1920, 1080), false),
        new("\\\\.\\DISPLAY3", new Rectangle(3840, 0, 1920, 1080), false),
        new("\\\\.\\DISPLAY1", new Rectangle(0, 0, 1920, 1080), true),
    };

    [Fact]
    public void All_screens_is_the_union()
    {
        Assert.Equal(new Rectangle(0, 0, 5760, 1080), ScreenCapture.SelectArea("all-screens", Three, Point.Empty, null));
    }

    [Fact]
    public void Primary_is_the_primary_flag_not_the_first_item()
    {
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), ScreenCapture.SelectArea("primary", Three, new Point(4000, 10), null));
    }

    [Fact]
    public void Pointer_monitor_follows_the_pointer_and_falls_back_to_primary()
    {
        Assert.Equal(3840, ScreenCapture.SelectArea("pointer-monitor", Three, new Point(4000, 500), null)!.Value.X);
        Assert.Equal(0, ScreenCapture.SelectArea("pointer-monitor", Three, new Point(-5000, -5000), null)!.Value.X);
    }

    [Fact]
    public void Active_monitor_is_the_one_with_the_largest_overlap()
    {
        // Window mostly on DISPLAY2, a little on DISPLAY3.
        var window = new Rectangle(2500, 100, 1500, 600);
        Assert.Equal(1920, ScreenCapture.SelectArea("active-monitor", Three, new Point(10, 10), window)!.Value.X);
    }

    [Fact]
    public void Active_monitor_without_window_uses_pointer_monitor()
    {
        Assert.Equal(3840, ScreenCapture.SelectArea("active-monitor", Three, new Point(5000, 50), null)!.Value.X);
    }

    [Fact]
    public void No_monitor_or_unknown_mode_returns_null()
    {
        Assert.Null(ScreenCapture.SelectArea("primary", new List<MonitorInfo>(), Point.Empty, null));
        Assert.Null(ScreenCapture.SelectArea("diagonal", Three, Point.Empty, null));
    }
}

public class CaptureFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wcc-files-" + Guid.NewGuid().ToString("N"));

    public CaptureFilesTests() => Directory.CreateDirectory(this._dir);

    public void Dispose()
    {
        try { Directory.Delete(this._dir, true); } catch { }
    }

    [Fact]
    public void File_name_uses_local_time_and_no_forbidden_characters()
    {
        var name = CaptureFiles.MakeFileName("Capture", new DateTime(2026, 11, 23, 11, 30, 45), ".png");
        Assert.Equal("Capture_2026-11-23_11-30-45.png", name);
        Assert.Equal("CaptureVideo_2026-11-23_11-30-45.mp4", CaptureFiles.MakeFileName("Capture:Video?", new DateTime(2026, 11, 23, 11, 30, 45), ".mp4"));
    }

    [Fact]
    public void Unique_path_never_overwrites()
    {
        File.WriteAllText(Path.Combine(this._dir, "a.png"), "x");
        File.WriteAllText(Path.Combine(this._dir, "a (2).png"), "x");
        Assert.EndsWith("a (3).png", CaptureFiles.UniquePath(this._dir, "a.png"));
    }

    [Fact]
    public void Latest_picks_most_recent_image_top_level_only()
    {
        var old = Touch("old.png", -60);
        var recent = Touch("recent.jpg", -5);
        Touch("newest.txt", 0);                      // not an image
        var sub = Directory.CreateDirectory(Path.Combine(this._dir, "sub"));
        File.WriteAllText(Path.Combine(sub.FullName, "deep.png"), "x");  // not scanned (top level only)

        var latest = CaptureFiles.FindLatest(new[] { this._dir, null, @"Z:\does\not\exist" }, CaptureFiles.ImageExtensions);

        Assert.Equal(recent, latest!.FullName);
        Assert.NotEqual(old, latest.FullName);
    }

    [Fact]
    public void Session_file_competes_with_folders()
    {
        Touch("folder.png", -30);
        var otherDir = Directory.CreateDirectory(Path.Combine(this._dir, "elsewhere")).FullName;
        var session = Path.Combine(otherDir, "session.png");
        File.WriteAllText(session, "x");

        Assert.Equal(session, CaptureFiles.FindLatest(new[] { this._dir }, CaptureFiles.ImageExtensions, new[] { session })!.FullName);
    }

    [Fact]
    public void Nothing_found_returns_null()
    {
        Assert.Null(CaptureFiles.FindLatest(new[] { this._dir }, CaptureFiles.VideoExtensions));
    }

    private string Touch(string name, int secondsFromNow)
    {
        var p = Path.Combine(this._dir, name);
        File.WriteAllText(p, "x");
        File.SetLastWriteTime(p, DateTime.Now.AddSeconds(secondsFromNow));
        return p;
    }
}
