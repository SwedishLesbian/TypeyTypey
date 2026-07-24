using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TypeyTypey;

internal sealed class MainForm : Form
{
    private readonly CheckBox _ctrl = new() { Text = "Ctrl", AutoSize = true };
    private readonly CheckBox _alt = new() { Text = "Alt", AutoSize = true };
    private readonly CheckBox _shift = new() { Text = "Shift", AutoSize = true };
    private readonly CheckBox _win = new() { Text = "Win", AutoSize = true };
    private readonly ComboBox _key = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly NumericUpDown _delay = new() { Minimum = 0, Maximum = 250, Increment = 5, Width = 80 };
    private readonly CheckBox _clearClipboard = new() { Text = "Clear clipboard after typing", AutoSize = true };
    private readonly CheckBox _startMinimized = new() { Text = "Start minimized to tray", AutoSize = true };
    private readonly Button _save = new() { Text = "Save and activate", AutoSize = true };
    private readonly Button _typeNow = new() { Text = "Type clipboard now", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Text = "Not active" };

    private readonly NotifyIcon _trayIcon;
    private AppSettings _settings;
    private CancellationTokenSource? _typingCts;
    private bool _allowClose;
    private bool _hotkeyRegistered;

    public MainForm()
    {
        _settings = AppSettings.Load();

        Text = "TypeyTypey";
        ClientSize = new Size(460, 275);
        MinimumSize = new Size(480, 315);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        foreach (Keys key in BuildKeyList())
            _key.Items.Add(new KeyChoice(key));

        BuildLayout();
        LoadSettingsIntoControls();

        _save.Click += (_, _) => SaveAndActivate();
        _typeNow.Click += async (_, _) => await TriggerTypingAsync();
        FormClosing += OnFormClosing;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
                HideToTray();
        };

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Type clipboard now", null, async (_, _) => await TriggerTypingAsync());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Text = "TypeyTypey",
            Icon = SystemIcons.Application,
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        Shown += (_, _) =>
        {
            SaveAndActivate(showSuccessMessage: false);
            if (_settings.StartMinimized)
                HideToTray();
        };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == HotkeyManager.WmHotkey && m.WParam.ToInt32() == HotkeyManager.HotkeyId)
        {
            _ = TriggerTypingAsync();
            return;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _typingCts?.Cancel();
            if (_hotkeyRegistered && IsHandleCreated)
                HotkeyManager.UnregisterHotKey(Handle, HotkeyManager.HotkeyId);
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 7,
            AutoSize = true
        };

        var title = new Label
        {
            Text = "TypeyTypey",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true
        };

        var explanation = new Label
        {
            Text = "Press the global hotkey to type clipboard text into the active window.",
            AutoSize = true
        };

        var hotkeyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        hotkeyRow.Controls.Add(new Label { Text = "Hotkey:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        hotkeyRow.Controls.AddRange([_ctrl, _alt, _shift, _win, _key]);

        var delayRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        delayRow.Controls.Add(new Label { Text = "Delay between characters:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        delayRow.Controls.Add(_delay);
        delayRow.Controls.Add(new Label { Text = "ms", AutoSize = true, Margin = new Padding(4, 6, 0, 0) });

        var optionsRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
        optionsRow.Controls.Add(_clearClipboard);
        optionsRow.Controls.Add(_startMinimized);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        buttons.Controls.Add(_save);
        buttons.Controls.Add(_typeNow);

        var statusRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        statusRow.Controls.Add(new Label { Text = "Status:", AutoSize = true });
        statusRow.Controls.Add(_status);

        root.Controls.Add(title);
        root.Controls.Add(explanation);
        root.Controls.Add(hotkeyRow);
        root.Controls.Add(delayRow);
        root.Controls.Add(optionsRow);
        root.Controls.Add(buttons);
        root.Controls.Add(statusRow);
        Controls.Add(root);
    }

    private void LoadSettingsIntoControls()
    {
        _ctrl.Checked = _settings.Ctrl;
        _alt.Checked = _settings.Alt;
        _shift.Checked = _settings.Shift;
        _win.Checked = _settings.Win;
        _delay.Value = Math.Clamp(_settings.CharacterDelayMs, 0, 250);
        _clearClipboard.Checked = _settings.ClearClipboardAfterTyping;
        _startMinimized.Checked = _settings.StartMinimized;

        int index = _key.Items.Cast<KeyChoice>().ToList().FindIndex(choice => choice.Key == _settings.Key);
        _key.SelectedIndex = index >= 0 ? index : 0;
    }

    private void SaveAndActivate(bool showSuccessMessage = true)
    {
        if (_key.SelectedItem is not KeyChoice selectedKey)
            return;

        var modifiers = HotkeyModifiers.NoRepeat;
        if (_ctrl.Checked) modifiers |= HotkeyModifiers.Ctrl;
        if (_alt.Checked) modifiers |= HotkeyModifiers.Alt;
        if (_shift.Checked) modifiers |= HotkeyModifiers.Shift;
        if (_win.Checked) modifiers |= HotkeyModifiers.Win;

        if ((modifiers & ~HotkeyModifiers.NoRepeat) == HotkeyModifiers.None)
        {
            MessageBox.Show("Choose at least one modifier key.", "TypeyTypey",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_hotkeyRegistered)
        {
            HotkeyManager.UnregisterHotKey(Handle, HotkeyManager.HotkeyId);
            _hotkeyRegistered = false;
        }

        if (!HotkeyManager.RegisterHotKey(Handle, HotkeyManager.HotkeyId, modifiers, (uint)selectedKey.Key))
        {
            int error = Marshal.GetLastWin32Error();
            _status.Text = "Hotkey unavailable";
            MessageBox.Show(
                $"Windows would not register that hotkey. Another program may already be using it.\n\nError: {new Win32Exception(error).Message}",
                "TypeyTypey", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _hotkeyRegistered = true;
        _settings = new AppSettings
        {
            Ctrl = _ctrl.Checked,
            Alt = _alt.Checked,
            Shift = _shift.Checked,
            Win = _win.Checked,
            Key = selectedKey.Key,
            CharacterDelayMs = (int)_delay.Value,
            ClearClipboardAfterTyping = _clearClipboard.Checked,
            StartMinimized = _startMinimized.Checked
        };
        _settings.Save();
        _status.Text = $"Active: {FormatHotkey(_settings)}";

        if (showSuccessMessage)
            _trayIcon.ShowBalloonTip(1500, "TypeyTypey", $"Hotkey active: {FormatHotkey(_settings)}", ToolTipIcon.Info);
    }

    private async Task TriggerTypingAsync()
    {
        if (_typingCts is not null)
            return;

        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : string.Empty;
        }
        catch (ExternalException ex)
        {
            ShowError("The clipboard is temporarily busy.", ex);
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            _status.Text = "Clipboard has no text";
            return;
        }

        _typingCts = new CancellationTokenSource();
        _status.Text = $"Typing {text.Length} characters…";

        try
        {
            await InputTyper.WaitForModifierReleaseAsync(_typingCts.Token);
            await InputTyper.TypeTextAsync(text, _settings.CharacterDelayMs, _typingCts.Token);

            if (_settings.ClearClipboardAfterTyping)
            {
                try { Clipboard.Clear(); }
                catch (ExternalException) { /* Clipboard contention is non-fatal after typing. */ }
            }

            _status.Text = $"Typed {text.Length} characters";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Typing cancelled";
        }
        catch (Exception ex)
        {
            _status.Text = "Typing failed";
            ShowError("TypeyTypey could not send the keystrokes.", ex);
        }
        finally
        {
            _typingCts.Dispose();
            _typingCts = null;
        }
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || e.CloseReason == CloseReason.WindowsShutDown)
            return;

        e.Cancel = true;
        HideToTray();
    }

    private static void ShowError(string message, Exception ex) =>
        MessageBox.Show($"{message}\n\n{ex.Message}", "TypeyTypey",
            MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static string FormatHotkey(AppSettings settings)
    {
        var parts = new List<string>();
        if (settings.Ctrl) parts.Add("Ctrl");
        if (settings.Alt) parts.Add("Alt");
        if (settings.Shift) parts.Add("Shift");
        if (settings.Win) parts.Add("Win");
        parts.Add(settings.Key.ToString());
        return string.Join(" + ", parts);
    }

    private static IEnumerable<Keys> BuildKeyList()
    {
        for (Keys key = Keys.A; key <= Keys.Z; key++) yield return key;
        for (Keys key = Keys.D0; key <= Keys.D9; key++) yield return key;
        for (Keys key = Keys.F1; key <= Keys.F12; key++) yield return key;

        yield return Keys.Insert;
        yield return Keys.Home;
        yield return Keys.End;
        yield return Keys.PageUp;
        yield return Keys.PageDown;
        yield return Keys.Space;
    }

    private sealed record KeyChoice(Keys Key)
    {
        public override string ToString() => Key switch
        {
            >= Keys.D0 and <= Keys.D9 => ((int)Key - (int)Keys.D0).ToString(),
            _ => Key.ToString()
        };
    }
}
