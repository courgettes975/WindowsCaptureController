using System.Runtime.InteropServices;

namespace WindowsCaptureController.Native;

/// <summary>
/// Sends documented keyboard shortcuts with SendInput.
/// Used only for shortcuts that Windows documents globally (Win+Shift+S, Win+Shift+R, Win+PrtScn)
/// or, later, for in-app shortcuts after the foreground window has been verified.
/// </summary>
public static class KeyboardInput
{
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12;     // Alt
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_SNAPSHOT = 0x2C; // Print Screen
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RWIN = 0x5C;
    public const ushort VK_R = 0x52;
    public const ushort VK_S = 0x53;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // The union must be as large as MOUSEINPUT so that sizeof(INPUT) is correct (40 bytes on x64).
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    private static readonly ushort[] ExtendedKeys = { VK_LWIN, VK_RWIN, VK_SNAPSHOT };

    /// <summary>Presses the keys in order, then releases them in reverse order.</summary>
    /// <returns>true when Windows accepted every event (false e.g. when blocked by UIPI).</returns>
    public static bool SendChord(params ushort[] keys)
    {
        var inputs = new List<INPUT>(keys.Length * 2);
        inputs.AddRange(keys.Select(k => Key(k, up: false)));
        inputs.AddRange(Enumerable.Reverse(keys).Select(k => Key(k, up: true)));
        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        return sent == inputs.Count;
    }

    /// <summary>True when a modifier is physically held (we wait for release to avoid corrupting the chord).</summary>
    public static bool AnyModifierDown() =>
        new[] { VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN }.Any(k => (Win32.GetAsyncKeyState(k) & 0x8000) != 0);

    private static INPUT Key(ushort vk, bool up)
    {
        var flags = up ? KEYEVENTF_KEYUP : 0u;
        if (ExtendedKeys.Contains(vk))
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } },
        };
    }
}
