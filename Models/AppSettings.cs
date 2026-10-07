namespace Kopiraiter.Models;

internal sealed record AppSettings
{
    public int Version { get; init; } = 2;

    public List<SnippetDefinition> Snippets { get; init; } = [];
}
