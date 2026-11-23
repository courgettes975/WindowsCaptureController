using System.Runtime.InteropServices;
using WindowsCaptureController.Native;

namespace WindowsCaptureController.Storage;

/// <summary>
/// Windows Known Folders (documented KNOWNFOLDERID values). Resolves redirections (e.g. Pictures on D:)
/// and OneDrive backup, and never depends on the display language.
/// </summary>
public static class KnownFolders
{
    public static readonly Guid Screenshots = new("b7bede81-df94-4682-a7d8-57a52620b86f");
    public static readonly Guid Pictures = new("33E28130-4E1E-4676-835A-98395C3BC3BB");
    public static readonly Guid Videos = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC");

    /// <summary>Folder name used by Windows for recordings inside Videos (documented on support.microsoft.com).</summary>
    public const string ScreenRecordingsSubfolder = "Screen Recordings";

    public static string? GetPath(Guid id)
    {
        IntPtr p = IntPtr.Zero;
        try
        {
            // KF_FLAG_DONT_VERIFY (0x4000): return the path even if the folder does not exist yet.
            return Win32.SHGetKnownFolderPath(id, 0x4000, IntPtr.Zero, out p) == 0 ? Marshal.PtrToStringUni(p) : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (p != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(p);
            }
        }
    }

    public static string? ScreenshotsPath => GetPath(Screenshots);

    public static string? VideosPath => GetPath(Videos);

    /// <summary>Videos\Screen Recordings. Exact folder used by the Snipping Tool is confirmed in phase 5.</summary>
    public static string? ScreenRecordingsPath =>
        VideosPath is { } v ? Path.Combine(v, ScreenRecordingsSubfolder) : null;
}
