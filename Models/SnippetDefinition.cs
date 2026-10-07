using System.Windows.Forms;
using Kopiraiter.Services;

namespace Kopiraiter.Models;

internal sealed record SnippetDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Keys Key { get; init; }

    public HotkeyModifiers Modifiers { get; init; }

    public string Text { get; init; } = string.Empty;
}
