using System.ComponentModel;
using System.Runtime.InteropServices;
using Kopiraiter.Models;
using Kopiraiter.Services;

namespace Kopiraiter;

internal sealed class MainForm : Form
{
    private const int HotkeyId = 1;
    private const int MaxTextLength = 10_000;

    private readonly SettingsStore _settingsStore;
    private readonly NativeTextInjector _textInjector;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly TextBox _hotkeyBox = new();
    private readonly TextBox _textBox = new();
    private readonly Button _saveButton = new();
    private readonly Label _statusLabel = new();

    private Keys _selectedKey = Keys.F8;
    private HotkeyModifiers _selectedModifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    private Keys _registeredKey = Keys.None;
    private HotkeyModifiers _registeredModifiers = HotkeyModifiers.None;
    private string _activeText = string.Empty;
    private bool _isCapturing;
    private bool _isInjecting;

    public MainForm(SettingsStore settingsStore, NativeTextInjector textInjector)
    {
        _settingsStore = settingsStore;
        _textInjector = textInjector;

        Text = "Копирайтер";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 440);
        Size = new Size(680, 520);
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildInterface();
        UpdateHotkeyDisplay();
    }

    protected override async void OnLoad(EventArgs eventArgs)
    {
        base.OnLoad(eventArgs);

        var result = await _settingsStore.LoadAsync(_lifetimeCancellation.Token);
        if (!result.IsSuccess || result.Value is null)
        {
            SetStatus(result.Error ?? "Не удалось загрузить настройки.", isError: true);
            return;
        }

        _selectedKey = result.Value.Key;
        _selectedModifiers = result.Value.Modifiers;
        _textBox.Text = result.Value.Text;
        UpdateHotkeyDisplay();

        if (string.IsNullOrWhiteSpace(_textBox.Text))
        {
            SetStatus("Выберите сочетание, введите текст и нажмите «Сохранить».");
            return;
        }

        if (TryReplaceRegisteredHotkey(_selectedKey, _selectedModifiers, out var error))
        {
            _activeText = _textBox.Text;
            SetStatus($"Активно: {FormatHotkey(_selectedKey, _selectedModifiers)}");
        }
        else
        {
            SetStatus(error, isError: true);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs eventArgs)
    {
        _lifetimeCancellation.Cancel();
        UnregisterCurrentHotkey();
        _lifetimeCancellation.Dispose();
        base.OnFormClosed(eventArgs);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey && message.WParam.ToInt32() == HotkeyId && !_isCapturing)
        {
            _ = InjectAfterHotkeyReleasedAsync();
        }

        base.WndProc(ref message);
    }

    private void BuildInterface()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 7
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            Text = "Быстрый ввод текста",
            Margin = new Padding(0, 0, 0, 6)
        };

        var description = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Назначьте горячую клавишу. Она введёт сохранённый текст в активное поле любого приложения.",
            Margin = new Padding(0, 0, 0, 20)
        };

        var hotkeyLabel = new Label
        {
            AutoSize = true,
            Text = "Сочетание клавиш",
            Margin = new Padding(0, 0, 0, 6)
        };

        _hotkeyBox.Dock = DockStyle.Top;
        _hotkeyBox.ReadOnly = true;
        _hotkeyBox.ShortcutsEnabled = false;
        _hotkeyBox.BackColor = SystemColors.Window;
        _hotkeyBox.Cursor = Cursors.Hand;
        _hotkeyBox.Margin = new Padding(0, 0, 0, 18);
        _hotkeyBox.Enter += (_, _) =>
        {
            _isCapturing = true;
            _hotkeyBox.Text = "Нажмите новое сочетание…";
            _hotkeyBox.SelectAll();
        };
        _hotkeyBox.Leave += (_, _) =>
        {
            _isCapturing = false;
            UpdateHotkeyDisplay();
        };
        _hotkeyBox.KeyDown += CaptureHotkey;

        var textLabel = new Label
        {
            AutoSize = true,
            Text = "Текст для ввода",
            Margin = new Padding(0, 0, 0, 6)
        };

        _textBox.Dock = DockStyle.Fill;
        _textBox.Multiline = true;
        _textBox.AcceptsReturn = true;
        _textBox.ScrollBars = ScrollBars.Vertical;
        _textBox.MaxLength = MaxTextLength;
        _textBox.Margin = new Padding(0, 0, 0, 18);
        _textBox.PlaceholderText = "Например: Спасибо! Я отвечу вам в течение рабочего дня.";

        _saveButton.AutoSize = true;
        _saveButton.Text = "Сохранить и включить";
        _saveButton.Padding = new Padding(12, 4, 12, 4);
        _saveButton.Anchor = AnchorStyles.Left;
        _saveButton.Margin = new Padding(0, 0, 0, 14);
        _saveButton.Click += SaveButtonClick;

        _statusLabel.AutoSize = true;
        _statusLabel.Text = "Загрузка…";
        _statusLabel.Margin = new Padding(0);

        layout.Controls.Add(title);
        layout.Controls.Add(description);
        layout.Controls.Add(hotkeyLabel);
        layout.Controls.Add(_hotkeyBox);
        layout.Controls.Add(textLabel);
        layout.Controls.Add(_textBox);

        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        footer.Controls.Add(_saveButton);
        footer.Controls.Add(_statusLabel);
        layout.Controls.Add(footer);

        Controls.Add(layout);
        AcceptButton = _saveButton;
    }

    private void CaptureHotkey(object? sender, KeyEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;

        if (eventArgs.KeyCode == Keys.Escape)
        {
            _isCapturing = false;
            ActiveControl = _textBox;
            UpdateHotkeyDisplay();
            return;
        }

        if (eventArgs.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu)
        {
            _hotkeyBox.Text = "Добавьте основную клавишу…";
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (eventArgs.Control)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (eventArgs.Alt)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (eventArgs.Shift)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        var isFunctionKey = eventArgs.KeyCode is >= Keys.F1 and <= Keys.F24;
        var hasSafeModifier = modifiers.HasFlag(HotkeyModifiers.Control)
                              || modifiers.HasFlag(HotkeyModifiers.Alt);
        if (!isFunctionKey && !hasSafeModifier)
        {
            SetStatus("Для букв и цифр добавьте Ctrl или Alt. Без модификатора разрешены F1–F24.", isError: true);
            return;
        }

        _selectedKey = eventArgs.KeyCode;
        _selectedModifiers = modifiers;
        _isCapturing = false;
        ActiveControl = _textBox;
        UpdateHotkeyDisplay();
        SetStatus("Новое сочетание выбрано. Нажмите «Сохранить и включить».");
    }

    private async void SaveButtonClick(object? sender, EventArgs eventArgs)
    {
        if (_selectedKey == Keys.None)
        {
            SetStatus("Сначала выберите сочетание клавиш.", isError: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(_textBox.Text))
        {
            SetStatus("Введите текст, который нужно печатать.", isError: true);
            _textBox.Focus();
            return;
        }

        _saveButton.Enabled = false;
        try
        {
            if (!TryReplaceRegisteredHotkey(_selectedKey, _selectedModifiers, out var hotkeyError))
            {
                SetStatus(hotkeyError, isError: true);
                return;
            }

            _activeText = _textBox.Text;

            var settings = new AppSettings
            {
                Key = _selectedKey,
                Modifiers = _selectedModifiers,
                Text = _textBox.Text
            };

            var saveResult = await _settingsStore.SaveAsync(settings, _lifetimeCancellation.Token);
            if (!saveResult.IsSuccess)
            {
                SetStatus(saveResult.Error ?? "Не удалось сохранить настройки.", isError: true);
                return;
            }

            SetStatus($"Активно: {FormatHotkey(_selectedKey, _selectedModifiers)}");
        }
        catch (OperationCanceledException)
        {
            // The application is closing.
        }
        finally
        {
            if (!IsDisposed)
            {
                _saveButton.Enabled = true;
            }
        }
    }

    private bool TryReplaceRegisteredHotkey(
        Keys key,
        HotkeyModifiers modifiers,
        out string error)
    {
        var previousKey = _registeredKey;
        var previousModifiers = _registeredModifiers;
        UnregisterCurrentHotkey();

        var nativeModifiers = (uint)(modifiers | HotkeyModifiers.NoRepeat);
        if (NativeMethods.RegisterHotKey(Handle, HotkeyId, nativeModifiers, (uint)key))
        {
            _registeredKey = key;
            _registeredModifiers = modifiers;
            error = string.Empty;
            return true;
        }

        var nativeError = new Win32Exception(Marshal.GetLastWin32Error()).Message;
        RestorePreviousHotkey(previousKey, previousModifiers);
        error = $"Не удалось назначить {FormatHotkey(key, modifiers)}. Возможно, сочетание занято. {nativeError}";
        return false;
    }

    private void RestorePreviousHotkey(Keys key, HotkeyModifiers modifiers)
    {
        if (key == Keys.None)
        {
            return;
        }

        if (NativeMethods.RegisterHotKey(
                Handle,
                HotkeyId,
                (uint)(modifiers | HotkeyModifiers.NoRepeat),
                (uint)key))
        {
            _registeredKey = key;
            _registeredModifiers = modifiers;
        }
    }

    private void UnregisterCurrentHotkey()
    {
        if (_registeredKey == Keys.None || !IsHandleCreated)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        _registeredKey = Keys.None;
        _registeredModifiers = HotkeyModifiers.None;
    }

    private async Task InjectAfterHotkeyReleasedAsync()
    {
        if (_isInjecting)
        {
            return;
        }

        _isInjecting = true;
        try
        {
            for (var attempt = 0; attempt < 150 && IsHotkeyPressed(); attempt++)
            {
                await Task.Delay(10, _lifetimeCancellation.Token);
            }

            var result = _textInjector.Inject(_activeText);
            if (!result.IsSuccess)
            {
                SetStatus(result.Error ?? "Не удалось ввести текст.", isError: true);
            }
        }
        catch (OperationCanceledException)
        {
            // The application is closing.
        }
        finally
        {
            _isInjecting = false;
        }
    }

    private bool IsHotkeyPressed()
    {
        if (IsKeyPressed((int)_registeredKey))
        {
            return true;
        }

        return (_registeredModifiers.HasFlag(HotkeyModifiers.Control) && IsKeyPressed(NativeMethods.VkControl))
               || (_registeredModifiers.HasFlag(HotkeyModifiers.Alt) && IsKeyPressed(NativeMethods.VkMenu))
               || (_registeredModifiers.HasFlag(HotkeyModifiers.Shift) && IsKeyPressed(NativeMethods.VkShift));
    }

    private static bool IsKeyPressed(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void UpdateHotkeyDisplay()
    {
        _hotkeyBox.Text = FormatHotkey(_selectedKey, _selectedModifiers);
        _hotkeyBox.SelectionStart = _hotkeyBox.TextLength;
    }

    private void SetStatus(string message, bool isError = false)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = isError ? Color.Firebrick : Color.SeaGreen;
    }

    private static string FormatHotkey(Keys key, HotkeyModifiers modifiers)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(key switch
        {
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + ((int)key - (int)Keys.D0))).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => $"Num {(int)key - (int)Keys.NumPad0}",
            _ => key.ToString()
        });

        return string.Join(" + ", parts);
    }
}
