using System.Text.Json;
using System.Windows.Forms;
using Kopiraiter.Models;

namespace Kopiraiter.Services;

internal sealed class SettingsStore(string settingsPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<OperationResult<AppSettings>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(settingsPath))
        {
            return OperationResult<AppSettings>.Ok(new AppSettings());
        }

        try
        {
            await using var stream = File.OpenRead(settingsPath);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

            var settings = document.RootElement.TryGetProperty("Snippets", out _)
                ? document.RootElement.Deserialize<AppSettings>(JsonOptions)
                : MigrateLegacySettings(document.RootElement);

            return settings is null
                ? OperationResult<AppSettings>.Fail("Файл настроек пуст.")
                : OperationResult<AppSettings>.Ok(Normalize(settings));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return OperationResult<AppSettings>.Fail($"Не удалось прочитать настройки: {exception.Message}");
        }
    }

    public async Task<OperationResult<bool>> SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = $"{settingsPath}.tmp";
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, settingsPath, overwrite: true);
            return OperationResult<bool>.Ok(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return OperationResult<bool>.Fail($"Не удалось сохранить настройки: {exception.Message}");
        }
    }

    private static AppSettings? MigrateLegacySettings(JsonElement root)
    {
        var legacy = root.Deserialize<LegacySettings>(JsonOptions);
        if (legacy is null)
        {
            return null;
        }

        var snippets = string.IsNullOrWhiteSpace(legacy.Text) || legacy.Key == Keys.None
            ? []
            : new List<SnippetDefinition>
            {
                new()
                {
                    Key = legacy.Key,
                    Modifiers = legacy.Modifiers,
                    Text = legacy.Text
                }
            };

        return new AppSettings { Snippets = snippets };
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var usedIds = new HashSet<Guid>();
        var snippets = new List<SnippetDefinition>(settings.Snippets.Count);

        foreach (var snippet in settings.Snippets)
        {
            var id = snippet.Id;
            if (id == Guid.Empty || !usedIds.Add(id))
            {
                id = Guid.NewGuid();
                usedIds.Add(id);
            }

            snippets.Add(snippet with { Id = id });
        }

        return settings with { Version = 2, Snippets = snippets };
    }

    private sealed record LegacySettings
    {
        public Keys Key { get; init; }

        public HotkeyModifiers Modifiers { get; init; }

        public string Text { get; init; } = string.Empty;
    }
}
