using System.ComponentModel;
using System.Runtime.InteropServices;
using Kopiraiter.Models;
using Kopiraiter.Services;

namespace Kopiraiter;

internal sealed class MainForm : Form
{
    private const int FirstHotkeyId = 1000;
    private const int MaxTextLength = 10_000;

    private readonly SettingsStore _settingsStore;
    private readonly NativeTextInjector _textInjector;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Dictionary<int, SnippetDefinition> _registeredHotkeys = [];
    private readonly Dictionary<Guid, string> _registrationErrors = [];
    private readonly TextBox _hotkeyBox = new();
    private readonly TextBox _textBox = new();
    private readonly Button _saveButton = new();
    private readonly Button _newButton = new();
    private readonly Button _deleteButton = new();
    private readonly DataGridView _savedGrid = new();
    private readonly Label _savedLabel = new();
    private readonly Label _statusLabel = new();
    private readonly NotifyIcon _trayIcon;

    private AppSettings _settings = new();
    private Guid? _editingId;
    private Keys _selectedKey = Keys.F8;
    private HotkeyModifiers _selectedModifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    private bool _isCapturing;
    private bool _isInjecting;
    private bool _isRefreshingGrid;
    private bool _allowExit;
    private bool _trayHintShown;

    public MainForm(SettingsStore settingsStore, NativeTextInjector textInjector)
    {
        _settingsStore = settingsStore;
        _textInjector = textInjector;

        Text = "Копирайтер";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 620);
        Size = new Size(860, 760);
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildInterface();
        _trayIcon = BuildTrayIcon();
        UpdateHotkeyDisplay();
        Deactivate += (_, _) => EndHotkeyCapture();
    }

    protected override async void OnLoad(EventArgs eventArgs)
    {
        base.OnLoad(eventArgs);

        try
        {
            var result = await _settingsStore.LoadAsync(_lifetimeCancellation.Token);
            if (!result.IsSuccess || result.Value is null)
            {
                SetStatus(result.Error ?? "Не удалось загрузить настройки.", isError: true);
                RefreshSavedGrid();
                return;
            }

            _settings = result.Value;
            RegisterAllHotkeys();
            RefreshSavedGrid();
            ShowRegistrationSummary();
        }
        catch (OperationCanceledException)
        {
            // The application is closing.
        }
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);

        if (WindowState == FormWindowState.Minimized)
        {
            HideToTray();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (!_allowExit && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            HideToTray();
            return;
        }

        base.OnFormClosing(eventArgs);
    }

    protected override void OnFormClosed(FormClosedEventArgs eventArgs)
    {
        _lifetimeCancellation.Cancel();
        UnregisterAllHotkeys();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _lifetimeCancellation.Dispose();
        base.OnFormClosed(eventArgs);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey
            && !_isCapturing
            && _registeredHotkeys.TryGetValue(message.WParam.ToInt32(), out var snippet))
        {
            _ = InjectAfterHotkeyReleasedAsync(snippet);
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
            RowCount = 10
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
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
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = "Добавьте сочетания и тексты. Закрытие окна сворачивает приложение в системный трей.",
            Margin = new Padding(0, 0, 0, 18)
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
        _hotkeyBox.Margin = new Padding(0, 0, 0, 14);
        _hotkeyBox.Enter += (_, _) => BeginHotkeyCapture();
        _hotkeyBox.Leave += (_, _) => EndHotkeyCapture();
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
        _textBox.Margin = new Padding(0, 0, 0, 12);
        _textBox.PlaceholderText = "Например: Спасибо! Я отвечу вам в течение рабочего дня.";

        ConfigureButtons();
        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 16)
        };
        buttonPanel.Controls.Add(_saveButton);
        buttonPanel.Controls.Add(_newButton);
        buttonPanel.Controls.Add(_deleteButton);

        _savedLabel.AutoSize = true;
        _savedLabel.Font = new Font(Font, FontStyle.Bold);
        _savedLabel.Text = "Сохранённые комбинации (0)";
        _savedLabel.Margin = new Padding(0, 0, 0, 6);

        ConfigureSavedGrid();

        _statusLabel.AutoSize = true;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Text = "Загрузка…";
        _statusLabel.Margin = new Padding(0, 10, 0, 0);

        layout.Controls.Add(title);
        layout.Controls.Add(description);
        layout.Controls.Add(hotkeyLabel);
        layout.Controls.Add(_hotkeyBox);
        layout.Controls.Add(textLabel);
        layout.Controls.Add(_textBox);
        layout.Controls.Add(buttonPanel);
        layout.Controls.Add(_savedLabel);
        layout.Controls.Add(_savedGrid);
        layout.Controls.Add(_statusLabel);

        Controls.Add(layout);
        AcceptButton = _saveButton;
    }

    private void ConfigureButtons()
    {
        _saveButton.AutoSize = true;
        _saveButton.Text = "Добавить комбинацию";
        _saveButton.Padding = new Padding(12, 4, 12, 4);
        _saveButton.Margin = new Padding(0, 0, 8, 0);
        _saveButton.Click += SaveButtonClick;

        _newButton.AutoSize = true;
        _newButton.Text = "Новая";
        _newButton.Padding = new Padding(12, 4, 12, 4);
        _newButton.Margin = new Padding(0, 0, 8, 0);
        _newButton.Click += (_, _) => ClearEditor();

        _deleteButton.AutoSize = true;
        _deleteButton.Text = "Удалить";
        _deleteButton.Padding = new Padding(12, 4, 12, 4);
        _deleteButton.Enabled = false;
        _deleteButton.Margin = new Padding(0);
        _deleteButton.Click += DeleteButtonClick;
    }

    private void ConfigureSavedGrid()
    {
        _savedGrid.Dock = DockStyle.Fill;
        _savedGrid.ReadOnly = true;
        _savedGrid.AllowUserToAddRows = false;
        _savedGrid.AllowUserToDeleteRows = false;
        _savedGrid.AllowUserToResizeRows = false;
        _savedGrid.AutoGenerateColumns = false;
        _savedGrid.BackgroundColor = SystemColors.Window;
        _savedGrid.BorderStyle = BorderStyle.Fixed3D;
        _savedGrid.MultiSelect = false;
        _savedGrid.RowHeadersVisible = false;
        _savedGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _savedGrid.Margin = new Padding(0);
        _savedGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Hotkey",
            HeaderText = "Сочетание",
            Width = 170,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _savedGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Text",
            HeaderText = "Текст",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _savedGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "Статус",
            Width = 165,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _savedGrid.SelectionChanged += SavedGridSelectionChanged;
        _savedGrid.CellDoubleClick += (_, _) => _textBox.Focus();
    }

    private NotifyIcon BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) =>
        {
            _allowExit = true;
            Close();
        });

        var trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Копирайтер — горячие клавиши",
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        return trayIcon;
    }

    private void BeginHotkeyCapture()
    {
        if (_isCapturing)
        {
            return;
        }

        _isCapturing = true;
        UnregisterAllHotkeys();
        _hotkeyBox.Text = "Нажмите новое сочетание…";
        _hotkeyBox.SelectAll();
    }

    private void EndHotkeyCapture()
    {
        if (!_isCapturing)
        {
            return;
        }

        _isCapturing = false;
        UpdateHotkeyDisplay();
        RegisterAllHotkeys();
        RefreshSavedGrid(_editingId);
    }

    private void CaptureHotkey(object? sender, KeyEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;

        if (eventArgs.KeyCode == Keys.Escape)
        {
            ActiveControl = _textBox;
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

        if (!IsSafeHotkey(eventArgs.KeyCode, modifiers))
        {
            SetStatus("Для букв и цифр добавьте Ctrl или Alt. Без модификатора разрешены F1–F24.", isError: true);
            return;
        }

        _selectedKey = eventArgs.KeyCode;
        _selectedModifiers = modifiers;
        ActiveControl = _textBox;
        UpdateHotkeyDisplay();
        SetStatus("Сочетание выбрано. Добавьте его или сохраните изменения.");
    }

    private async void SaveButtonClick(object? sender, EventArgs eventArgs)
    {
        if (!IsSafeHotkey(_selectedKey, _selectedModifiers))
        {
            SetStatus("Выберите корректное сочетание клавиш.", isError: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(_textBox.Text))
        {
            SetStatus("Введите текст, который нужно печатать.", isError: true);
            _textBox.Focus();
            return;
        }

        var duplicate = _settings.Snippets.FirstOrDefault(snippet =>
            snippet.Id != _editingId
            && snippet.Key == _selectedKey
            && snippet.Modifiers == _selectedModifiers);
        if (duplicate is not null)
        {
            SetStatus("Такое сочетание уже есть в списке.", isError: true);
            return;
        }

        var snippetId = _editingId ?? Guid.NewGuid();
        var candidate = new SnippetDefinition
        {
            Id = snippetId,
            Key = _selectedKey,
            Modifiers = _selectedModifiers,
            Text = _textBox.Text
        };

        var snippets = _settings.Snippets.ToList();
        var existingIndex = snippets.FindIndex(snippet => snippet.Id == snippetId);
        if (existingIndex >= 0)
        {
            snippets[existingIndex] = candidate;
        }
        else
        {
            snippets.Add(candidate);
        }

        await SaveSettingsAsync(
            _settings with { Snippets = snippets },
            candidate.Id,
            "Комбинация сохранена и активна.");
    }

    private async void DeleteButtonClick(object? sender, EventArgs eventArgs)
    {
        if (_editingId is not { } snippetId)
        {
            return;
        }

        var answer = MessageBox.Show(
            "Удалить выбранную комбинацию?",
            "Копирайтер",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        var snippets = _settings.Snippets
            .Where(snippet => snippet.Id != snippetId)
            .ToList();

        if (await SaveSettingsAsync(
                _settings with { Snippets = snippets },
                selectedId: null,
                successMessage: "Комбинация удалена."))
        {
            ClearEditor();
        }
    }

    private async Task<bool> SaveSettingsAsync(
        AppSettings updatedSettings,
        Guid? selectedId,
        string successMessage)
    {
        SetEditorEnabled(enabled: false);
        try
        {
            var result = await _settingsStore.SaveAsync(updatedSettings, _lifetimeCancellation.Token);
            if (!result.IsSuccess)
            {
                SetStatus(result.Error ?? "Не удалось сохранить настройки.", isError: true);
                return false;
            }

            _settings = updatedSettings;
            _editingId = selectedId;
            RegisterAllHotkeys();
            RefreshSavedGrid(selectedId);

            if (selectedId is { } id && _registrationErrors.TryGetValue(id, out var registrationError))
            {
                SetStatus($"Сохранено, но сочетание не активно: {registrationError}", isError: true);
            }
            else
            {
                SetStatus(successMessage);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            if (!IsDisposed)
            {
                SetEditorEnabled(enabled: true);
            }
        }
    }

    private void RegisterAllHotkeys()
    {
        UnregisterAllHotkeys();
        _registrationErrors.Clear();

        if (!IsHandleCreated || _isCapturing)
        {
            return;
        }

        var registeredCombinations = new HashSet<(Keys Key, HotkeyModifiers Modifiers)>();
        var hotkeyId = FirstHotkeyId;

        foreach (var snippet in _settings.Snippets)
        {
            if (!IsSafeHotkey(snippet.Key, snippet.Modifiers))
            {
                _registrationErrors[snippet.Id] = "Некорректное сочетание";
                continue;
            }

            if (!registeredCombinations.Add((snippet.Key, snippet.Modifiers)))
            {
                _registrationErrors[snippet.Id] = "Дубликат в списке";
                continue;
            }

            var registered = NativeMethods.RegisterHotKey(
                Handle,
                hotkeyId,
                (uint)(snippet.Modifiers | HotkeyModifiers.NoRepeat),
                (uint)snippet.Key);

            if (registered)
            {
                _registeredHotkeys[hotkeyId] = snippet;
                hotkeyId++;
                continue;
            }

            var nativeError = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            _registrationErrors[snippet.Id] = $"Занято: {nativeError}";
        }
    }

    private void UnregisterAllHotkeys()
    {
        if (IsHandleCreated)
        {
            foreach (var hotkeyId in _registeredHotkeys.Keys)
            {
                NativeMethods.UnregisterHotKey(Handle, hotkeyId);
            }
        }

        _registeredHotkeys.Clear();
    }

    private async Task InjectAfterHotkeyReleasedAsync(SnippetDefinition snippet)
    {
        if (_isInjecting)
        {
            return;
        }

        _isInjecting = true;
        try
        {
            for (var attempt = 0; attempt < 150 && IsHotkeyPressed(snippet); attempt++)
            {
                await Task.Delay(10, _lifetimeCancellation.Token);
            }

            if (IsHotkeyPressed(snippet))
            {
                SetStatus("Отпустите клавиши сочетания и попробуйте ещё раз.", isError: true);
                return;
            }

            var result = _textInjector.Inject(snippet.Text);
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

    private static bool IsHotkeyPressed(SnippetDefinition snippet)
    {
        if (IsKeyPressed((int)snippet.Key))
        {
            return true;
        }

        return (snippet.Modifiers.HasFlag(HotkeyModifiers.Control) && IsKeyPressed(NativeMethods.VkControl))
               || (snippet.Modifiers.HasFlag(HotkeyModifiers.Alt) && IsKeyPressed(NativeMethods.VkMenu))
               || (snippet.Modifiers.HasFlag(HotkeyModifiers.Shift) && IsKeyPressed(NativeMethods.VkShift));
    }

    private void RefreshSavedGrid(Guid? selectedId = null)
    {
        _isRefreshingGrid = true;
        try
        {
            _savedGrid.Rows.Clear();

            foreach (var snippet in _settings.Snippets)
            {
                var isActive = _registeredHotkeys.Values.Any(registered => registered.Id == snippet.Id);
                var status = isActive
                    ? "Активна"
                    : _registrationErrors.GetValueOrDefault(snippet.Id, "Не активна");
                var rowIndex = _savedGrid.Rows.Add(
                    FormatHotkey(snippet.Key, snippet.Modifiers),
                    CreateTextPreview(snippet.Text),
                    status);
                var row = _savedGrid.Rows[rowIndex];
                row.Tag = snippet.Id;
                row.Cells[2].Style.ForeColor = isActive ? Color.SeaGreen : Color.Firebrick;
            }

            _savedLabel.Text = $"Сохранённые комбинации ({_settings.Snippets.Count})";
            _savedGrid.ClearSelection();

            if (selectedId is { } id)
            {
                var row = _savedGrid.Rows
                    .Cast<DataGridViewRow>()
                    .FirstOrDefault(candidate => candidate.Tag is Guid candidateId && candidateId == id);
                if (row is not null)
                {
                    row.Selected = true;
                    _savedGrid.CurrentCell = row.Cells[0];
                }
            }
        }
        finally
        {
            _isRefreshingGrid = false;
        }
    }

    private void SavedGridSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_isRefreshingGrid || _savedGrid.SelectedRows.Count == 0)
        {
            return;
        }

        if (_savedGrid.SelectedRows[0].Tag is not Guid snippetId)
        {
            return;
        }

        var snippet = _settings.Snippets.FirstOrDefault(candidate => candidate.Id == snippetId);
        if (snippet is not null)
        {
            LoadSnippetIntoEditor(snippet);
        }
    }

    private void LoadSnippetIntoEditor(SnippetDefinition snippet)
    {
        _editingId = snippet.Id;
        _selectedKey = snippet.Key;
        _selectedModifiers = snippet.Modifiers;
        _textBox.Text = snippet.Text;
        _saveButton.Text = "Сохранить изменения";
        _deleteButton.Enabled = true;
        UpdateHotkeyDisplay();
    }

    private void ClearEditor()
    {
        _editingId = null;
        _selectedKey = Keys.F8;
        _selectedModifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        _textBox.Clear();
        _saveButton.Text = "Добавить комбинацию";
        _deleteButton.Enabled = false;
        _savedGrid.ClearSelection();
        UpdateHotkeyDisplay();
    }

    private void ShowRegistrationSummary()
    {
        if (_settings.Snippets.Count == 0)
        {
            SetStatus("Добавьте первую комбинацию. Настройки сохраняются автоматически.");
            return;
        }

        if (_registrationErrors.Count == 0)
        {
            SetStatus($"Активных комбинаций: {_registeredHotkeys.Count}. Приложение продолжит работать в трее.");
            return;
        }

        SetStatus(
            $"Активно: {_registeredHotkeys.Count}; требуют внимания: {_registrationErrors.Count}. Смотрите статусы в списке.",
            isError: true);
    }

    private void HideToTray()
    {
        EndHotkeyCapture();
        ShowInTaskbar = false;
        Hide();

        if (_trayHintShown)
        {
            return;
        }

        _trayHintShown = true;
        _trayIcon.ShowBalloonTip(
            3000,
            "Копирайтер работает",
            "Окно скрыто в трей. Сохранённые сочетания остаются активными.",
            ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void SetEditorEnabled(bool enabled)
    {
        _saveButton.Enabled = enabled;
        _newButton.Enabled = enabled;
        _deleteButton.Enabled = enabled && _editingId is not null;
    }

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

    private static bool IsSafeHotkey(Keys key, HotkeyModifiers modifiers)
    {
        var isFunctionKey = key is >= Keys.F1 and <= Keys.F24;
        var hasSafeModifier = modifiers.HasFlag(HotkeyModifiers.Control)
                              || modifiers.HasFlag(HotkeyModifiers.Alt);
        return key != Keys.None && (isFunctionKey || hasSafeModifier);
    }

    private static bool IsKeyPressed(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static string CreateTextPreview(string text)
    {
        var singleLine = text.Replace("\r\n", " ↵ ").Replace("\r", " ↵ ").Replace("\n", " ↵ ");
        return singleLine.Length <= 90 ? singleLine : $"{singleLine[..87]}…";
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
