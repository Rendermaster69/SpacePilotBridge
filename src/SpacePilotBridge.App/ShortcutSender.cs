using System.Runtime.InteropServices;

namespace SpacePilotBridge.App;

internal static class ShortcutSender
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // INPUT is a Win32 union. MOUSEINPUT must be present even when we only send
    // keyboard events, otherwise Marshal.SizeOf<INPUT>() is too small on x64 and
    // SendInput fails with ERROR_INVALID_PARAMETER.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private readonly record struct KeySpec(ushort Vk, bool Extended = false);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    public static int LastWin32Error { get; private set; }

    public static bool Send(string shortcut)
    {
        LastWin32Error = 0;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;
        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        var modifiers = new List<KeySpec>();
        KeySpec main = default;
        foreach (string part in parts)
        {
            KeySpec key = ParseKey(part);
            if (key.Vk == 0) return false;
            if (IsModifier(key.Vk)) modifiers.Add(key);
            else main = key;
        }
        if (main.Vk == 0) return false;

        var inputs = new List<INPUT>();
        foreach (KeySpec mod in modifiers) inputs.Add(Key(mod, false));
        inputs.Add(Key(main, false));
        inputs.Add(Key(main, true));
        for (int i = modifiers.Count - 1; i >= 0; i--) inputs.Add(Key(modifiers[i], true));

        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Count)
        {
            LastWin32Error = Marshal.GetLastWin32Error();
            return false;
        }
        return true;
    }


    public static bool SetKeyState(string keyName, bool down)
    {
        LastWin32Error = 0;
        KeySpec key = ParseKey(keyName);
        if (key.Vk == 0) return false;
        INPUT input = Key(key, up: !down);
        uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent != 1)
        {
            LastWin32Error = Marshal.GetLastWin32Error();
            return false;
        }
        return true;
    }

    private static INPUT Key(KeySpec key, bool up)
    {
        uint flags = (key.Extended ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0);
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = key.Vk, dwFlags = flags }
            }
        };
    }

    private static bool IsModifier(ushort vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B;

    private static KeySpec ParseKey(string token)
    {
        string t = token.Trim().ToUpperInvariant();
        if (t.Length == 1 && t[0] is >= 'A' and <= 'Z') return new((ushort)t[0]);
        if (t.Length == 1 && t[0] is >= '0' and <= '9') return new((ushort)t[0]);
        if (t.StartsWith('F') && int.TryParse(t[1..], out int fn) && fn is >= 1 and <= 24) return new((ushort)(0x70 + fn - 1));

        return t switch
        {
            "CTRL" or "CONTROL" => new(0x11),
            "SHIFT" => new(0x10),
            "ALT" => new(0x12),
            "WIN" or "WINDOWS" => new(0x5B, true),
            "ENTER" or "RETURN" => new(0x0D),
            "ESC" or "ESCAPE" => new(0x1B),
            "SPACE" => new(0x20),
            "TAB" => new(0x09),
            "BACKSPACE" => new(0x08),
            "DELETE" or "DEL" => new(0x2E, true),
            "INSERT" or "INS" => new(0x2D, true),
            "HOME" => new(0x24, true),
            "END" => new(0x23, true),
            "PAGEUP" or "PGUP" => new(0x21, true),
            "PAGEDOWN" or "PGDN" => new(0x22, true),
            "LEFT" => new(0x25, true),
            "UP" => new(0x26, true),
            "RIGHT" => new(0x27, true),
            "DOWN" => new(0x28, true),
            "PLUS" => new(0x6B),
            "MINUS" => new(0x6D),
            _ => default
        };
    }
}
