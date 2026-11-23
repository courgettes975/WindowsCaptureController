using WindowsCaptureController.Storage;

namespace WindowsCaptureController.Tests;

public class HistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wcc-hist-" + Guid.NewGuid().ToString("N"));

    public HistoryTests() => Directory.CreateDirectory(this._dir);

    public void Dispose()
    {
        try { Directory.Delete(this._dir, true); } catch { }
    }

    [Theory]
    [InlineData(@"..\secret.png")]
    [InlineData(@"..\..\Windows\win.ini")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("sub/a.png")]
    [InlineData("")]
    [InlineData("a?.png")]
    public void Only_bare_file_names_inside_the_folders_are_accepted(string name)
    {
        File.WriteAllText(Path.Combine(this._dir, "a.png"), "x");
        Assert.Null(CaptureFiles.ResolveInFolders(name, new[] { this._dir }));
    }

    [Fact]
    public void A_bare_name_is_found_in_the_first_folder_that_has_it()
    {
        var other = Directory.CreateDirectory(Path.Combine(this._dir, "videos")).FullName;
        File.WriteAllText(Path.Combine(other, "clip.mp4"), "x");
        Assert.Equal(Path.Combine(other, "clip.mp4"), CaptureFiles.ResolveInFolders("clip.mp4", new[] { this._dir, other })!.FullName);
    }

    [Fact]
    public void Recent_list_is_newest_first_bounded_and_deduplicated()
    {
        for (var i = 0; i < 5; i++)
        {
            var p = Path.Combine(this._dir, $"c{i}.png");
            File.WriteAllText(p, "x");
            File.SetLastWriteTime(p, DateTime.Now.AddMinutes(-i));
        }

        var list = CaptureFiles.ListRecent(new[] { this._dir, this._dir }, CaptureFiles.ImageExtensions, 3);

        Assert.Equal(new[] { "c0.png", "c1.png", "c2.png" }, list.Select(f => f.Name));
    }
}
