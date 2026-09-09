using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FastResetUpdated.Shared
{
    // Megabonk uses Rewired for its own input handling, which prevents
    // UnityEngine.Input.GetKeyDown from ever detecting a keypress in practice (confirmed:
    // neither configured hotkey did anything, even though OnUpdate is demonstrably running
    // every frame — the auto-reset logic keeps working). Rather than depend on whatever input
    // system the host game happens to use, our own hotkeys are read straight from the OS via
    // a small Win32 call, which works no matter what Unity/Rewired are doing internally.
    internal static class Win32Input
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKeyCode);

        // F1-F12 are consecutive Win32 virtual-key codes starting at 0x70; letters and digits
        // share their ASCII codes. This is the deliberately small set of keys this mod lets
        // you bind to — enough for any realistic hotkey choice without pulling in a full
        // keyboard-layout dependency.
        private static readonly Dictionary<string, int> NameToVirtualKey = BuildKeyMap();

        public static IEnumerable<string> SupportedKeyNames => NameToVirtualKey.Keys;

        public static bool TryGetVirtualKey(string keyName, out int virtualKeyCode)
        {
            if (!string.IsNullOrEmpty(keyName) &&
                NameToVirtualKey.TryGetValue(keyName.ToUpperInvariant(), out virtualKeyCode))
                return true;

            virtualKeyCode = 0;
            return false;
        }

        public static bool IsKeyDown(int virtualKeyCode)
        {
            // High bit set means the key is currently held down.
            return (GetAsyncKeyState(virtualKeyCode) & 0x8000) != 0;
        }

        // VK_SHIFT — used by the menu's +/- steppers to take a larger step while held, and
        // exposed here rather than as a magic number at each call site.
        public static bool IsShiftDown()
        {
            return IsKeyDown(0x10);
        }

        private static Dictionary<string, int> BuildKeyMap()
        {
            var map = new Dictionary<string, int>();

            for (int i = 1; i <= 12; i++)
                map[$"F{i}"] = 0x70 + (i - 1);

            for (char c = 'A'; c <= 'Z'; c++)
                map[c.ToString()] = c;

            for (char c = '0'; c <= '9'; c++)
                map[c.ToString()] = c;

            return map;
        }
    }
}
