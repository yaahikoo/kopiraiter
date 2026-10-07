using System.Windows.Forms;
using Kopiraiter.Services;

namespace Kopiraiter.Models;

internal sealed record AppSettings
{
    public Keys Key { get; init; } = Keys.F8;

    public HotkeyModifiers Modifiers { get; init; } = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    public string Text { get; init; } = string.Empty;
}
