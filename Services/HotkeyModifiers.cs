namespace Kopiraiter.Services;

[Flags]
internal enum HotkeyModifiers : uint
{
    None = 0x0000,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    NoRepeat = 0x4000
}
