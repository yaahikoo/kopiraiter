using Kopiraiter.Services;

namespace Kopiraiter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, @"Local\Kopiraiter", out var isFirstInstance);

        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Приложение «Копирайтер» уже запущено.",
                "Копирайтер",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kopiraiter",
            "settings.json");

        Application.Run(new MainForm(
            new SettingsStore(settingsPath),
            new NativeTextInjector()));
    }
}
