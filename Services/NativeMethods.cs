using System.Runtime.InteropServices;

namespace Kopiraiter.Services;

internal static class NativeMethods
{
    internal const int WmHotkey = 0x0312;
    internal const uint InputKeyboard = 1;
    internal const uint KeyEventKeyUp = 0x0002;
    internal const uint KeyEventUnicode = 0x0004;
    internal const int VkReturn = 0x0D;
    internal const int VkShift = 0x10;
    internal const int VkControl = 0x11;
    internal const int VkMenu = 0x12;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(
        uint inputCount,
        [MarshalAs(UnmanagedType.LPArray), In] Input[] inputs,
        int inputSize);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);
}

[StructLayout(LayoutKind.Sequential)]
internal struct Input
{
    internal uint Type;
    internal InputUnion Data;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)]
    internal MouseInput Mouse;

    [FieldOffset(0)]
    internal KeyboardInput Keyboard;

    [FieldOffset(0)]
    internal HardwareInput Hardware;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MouseInput
{
    internal int X;
    internal int Y;
    internal uint MouseData;
    internal uint Flags;
    internal uint Time;
    internal UIntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KeyboardInput
{
    internal ushort VirtualKey;
    internal ushort ScanCode;
    internal uint Flags;
    internal uint Time;
    internal UIntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HardwareInput
{
    internal uint Message;
    internal ushort ParamLow;
    internal ushort ParamHigh;
}
