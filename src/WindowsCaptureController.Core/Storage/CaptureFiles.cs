namespace WindowsCaptureController.Storage;

/// <summary>File naming and "latest capture" lookup (never recursive, never scans a whole disk).</summary>
public static class CaptureFiles
{
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".jxr", ".webp" };
    public static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".webm", ".mov" };

    /// <summary>Capture_2026-11-23_11-30-45.png — local time, only characters valid in Windows file names.</summary>
    public static string MakeFileName(string prefix, DateTime localTime, string extension)
    {
        var safePrefix = new string(prefix.Where(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0).ToArray());
        if (safePrefix.Length == 0)
        {
            safePrefix = "Capture";
        }

        return $"{safePrefix}_{localTime:yyyy-MM-dd_HH-mm-ss}{extension}";
    }

    /// <summary>Appends " (2)", " (3)"… when the name already exists.</summary>
    public static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
        }

        return path;
    }

    /// <summary>
    /// Most recent file (LastWriteTime, then CreationTime) with one of the extensions, in the given folders
    /// (top level only) plus explicitly known files (e.g. the capture saved in this session).
    /// Missing / inaccessible folders are skipped.
    /// </summary>
    /// <summary>Most recent files (top level of each folder only), newest first, at most <paramref name="max"/>.</summary>
    public static List<FileInfo> ListRecent(IEnumerable<string?> folders, IReadOnlyCollection<string> extensions, int max)
    {
        var all = new List<FileInfo>();
        foreach (var dir in folders.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    all.AddRange(new DirectoryInfo(dir!).EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                        .Where(f => extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase)));
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        return all
            .GroupBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(Math.Max(0, max))
            .ToList();
    }

    /// <summary>
    /// Resolves a bare file name inside the allowed folders only. Rejects anything containing a path
    /// separator, ".." or invalid characters, so a request can never open a file elsewhere on the disk.
    /// </summary>
    public static FileInfo? ResolveInFolders(string fileName, IEnumerable<string?> folders)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") ||
            fileName.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        foreach (var dir in folders.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            var p = Path.Combine(dir!, fileName);
            if (File.Exists(p))
            {
                return new FileInfo(p);
            }
        }

        return null;
    }

    public static FileInfo? FindLatest(IEnumerable<string?> folders, IReadOnlyCollection<string> extensions, IEnumerable<string?>? knownFiles = null)
    {
        var candidates = new List<FileInfo>();
        foreach (var dir in folders.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                candidates.AddRange(new DirectoryInfo(dir!).EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(f => extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase)));
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        foreach (var f in (knownFiles ?? Enumerable.Empty<string?>()).Where(File.Exists))
        {
            candidates.Add(new FileInfo(f!));
        }

        return candidates
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ThenByDescending(f => f.CreationTimeUtc)
            .FirstOrDefault();
    }
}
