using WindowsCaptureController.Diagnostics;
using WindowsCaptureController.Logging;
using WindowsCaptureController.WindowsCapture;

namespace WindowsCaptureController.Tests;

public class ActionDebouncerTests
{
    [Fact]
    public void Second_press_within_window_is_ignored_then_accepted_after()
    {
        long now = 1000;
        var d = new ActionDebouncer(400, () => now);

        Assert.True(d.TryEnter("capture"));
        now += 399;
        Assert.False(d.TryEnter("capture"));
        now += 1;
        Assert.True(d.TryEnter("capture"));
    }

    [Fact]
    public void Actions_are_debounced_independently()
    {
        long now = 0;
        var d = new ActionDebouncer(400, () => now);

        Assert.True(d.TryEnter("rectangle"));
        Assert.True(d.TryEnter("window"));
    }

    [Fact]
    public void A_rejected_press_does_not_extend_the_window()
    {
        long now = 0;
        var d = new ActionDebouncer(400, () => now);
        d.TryEnter("a");
        now = 300;
        d.TryEnter("a"); // ignored
        now = 400;
        Assert.True(d.TryEnter("a"));
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(100000, 5000)]
    public void Window_is_clamped_to_safe_range(int requested, int expected)
    {
        Assert.Equal(expected, new ActionDebouncer(requested).WindowMs);
    }
}

public class LogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wcc-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _previous = Log.Directory;

    public LogTests() => Log.Directory = this._dir;

    public void Dispose()
    {
        Log.Directory = this._previous;
        try { Directory.Delete(this._dir, true); } catch { }
    }

    [Fact]
    public void Rotation_keeps_at_most_five_files()
    {
        Directory.CreateDirectory(this._dir);
        for (var round = 0; round < 8; round++)
        {
            File.WriteAllBytes(Log.CurrentFile, new byte[Log.MaxFileBytes]);
            Log.RotateIfNeeded();
        }

        File.WriteAllText(Log.CurrentFile, "x");
        Assert.Equal(Log.MaxFiles, Directory.GetFiles(this._dir, "wcc*.log").Length);
    }

    [Fact]
    public void Levels_below_minimum_are_not_written()
    {
        var old = Log.MinimumLevel;
        Log.MinimumLevel = LogLevel.Warning;
        try
        {
            Log.Info("hidden");
            Log.Warning("shown");
            var text = File.ReadAllText(Log.CurrentFile);
            Assert.DoesNotContain("hidden", text);
            Assert.Contains("WARNING", text);
        }
        finally
        {
            Log.MinimumLevel = old;
        }
    }

    [Theory]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("TRACE", LogLevel.Trace)]
    [InlineData("nonsense", LogLevel.Info)]
    [InlineData(null, LogLevel.Info)]
    public void Invalid_level_falls_back_to_info(string? value, LogLevel expected)
    {
        Assert.Equal(expected, Log.ParseLevel(value));
    }
}

public class DiagnosticsTests
{
    [Fact]
    public void Profile_path_is_masked()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var masked = SystemDiagnostics.Mask(Path.Combine(profile, "Videos"));

        Assert.StartsWith("%USERPROFILE%", masked);
        Assert.DoesNotContain(Environment.UserName, masked, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Paths_outside_profile_are_kept()
    {
        Assert.Equal(@"D:\Image\Screenshots", SystemDiagnostics.Mask(@"D:\Image\Screenshots"));
    }

    [Fact]
    public void Protractor_reports_not_available_instead_of_faking()
    {
        var ctl = new WindowsCaptureController.WindowsCapture.WindowsCaptureController();
        Assert.Equal(ActionStatus.NotAvailable, ctl.SelectProtractor().Status);
    }
}

