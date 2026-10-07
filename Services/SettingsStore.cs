using System.Text.Json;
using Kopiraiter.Models;

namespace Kopiraiter.Services;

internal sealed class SettingsStore(string settingsPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
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
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                JsonOptions,
                cancellationToken);

            return settings is null
                ? OperationResult<AppSettings>.Fail("Файл настроек пуст.")
                : OperationResult<AppSettings>.Ok(settings);
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

            await using var stream = File.Create(settingsPath);
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
            return OperationResult<bool>.Ok(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return OperationResult<bool>.Fail($"Не удалось сохранить настройки: {exception.Message}");
        }
    }
}
