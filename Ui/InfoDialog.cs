namespace TypeyTypey;

/// <summary>
/// The Help and About windows. One class because they are the same window with different content:
/// a scrolling page of cards and a single dismiss button, owning no application state.
///
/// Help is reachable from the tray menu and from <c>--help</c>, and the command line route runs in a
/// process that may not be the primary instance, so this must not touch anything the running
/// instance owns.
/// </summary>
internal sealed class InfoDialog : Form
{
    private readonly List<Label> _wrapping = [];
    private readonly List<Label> _terms = [];
    private readonly TableLayoutPanel _page;
    private readonly AppTheme _theme;

    private InfoDialog(string title, Icon? icon, AppTheme theme, Size clientSize)
    {
        _theme = theme;
        // Same load-bearing bracket as SettingsForm: assigning AutoScaleMode resets
        // AutoScaleDimensions to the device value, so the scale pass must be deferred to
        // ResumeLayout or it computes a factor of 1.0. See AGENTS.md section 6.
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = title;
        if (icon is not null) Icon = icon;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        _page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(UiKit.PagePadding, UiKit.SpaceWide, UiKit.PagePadding, UiKit.PagePadding)
        };

        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(UiKit.PagePadding, UiKit.Space, UiKit.PagePadding, UiKit.SpaceWide)
        };
        bar.Controls.Add(close);

        Controls.Add(_page);
        Controls.Add(bar);
        AcceptButton = close;
        CancelButton = close;

        ClientSize = clientSize;
        MinimumSize = new Size(clientSize.Width - 140, 300);
        ResumeLayout(performLayout: true);

        // Size is in device pixels only after the scale pass, so the fit check belongs here rather
        // than alongside the ClientSize above.
        Size = WindowPlacement.FitToWorkingArea(Size, Screen.FromPoint(Cursor.Position).WorkingArea);
    }

    /// <summary>
    /// Sizes the term column, wraps the prose and paints the theme, in that order.
    ///
    /// This runs after the content is built, not in the constructor. Theming a card before it exists
    /// leaves it filled with the default window colour, which shows through the card's padding as a
    /// bright ring around otherwise dark content.
    /// </summary>
    private void Finish()
    {
        AlignTerms();
        RewrapLabels();
        ThemeManager.Apply(this, _theme);
    }

    /// <summary>
    /// Widens the term column to the longest term actually present. A fixed width cannot know the
    /// user's font or scaling, and silently clipped "--clear-history" and the --admintask variants
    /// down to indistinguishable stubs.
    /// </summary>
    private void AlignTerms()
    {
        if (_terms.Count == 0)
            return;

        int widest = 0;
        foreach (Label term in _terms)
            widest = Math.Max(widest, TextRenderer.MeasureText(term.Text, term.Font).Width);

        foreach (Label term in _terms)
            term.Width = widest + UiKit.Space;
    }

    /// <summary>
    /// Labels wrap by growing downward against a MaximumSize, which is the only way a WinForms label
    /// both wraps and reports its own height. That width is fixed at construction, so it is
    /// recomputed whenever the window is resized.
    /// </summary>
    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        RewrapLabels();
    }

    private void RewrapLabels()
    {
        foreach (Label label in _wrapping)
        {
            int available = label.Parent is null ? 0 : label.Parent.ClientSize.Width - label.Margin.Horizontal - label.Left;
            if (available > 80)
                label.MaximumSize = new Size(available, 0);
        }
    }

    // ---------- content ----------

    public static void ShowHelp(IWin32Window? owner, AppSettings settings, Icon? icon)
    {
        using var dialog = new InfoDialog("TypeyTypey Help", icon, settings.Theme, WindowPlacement.DefaultHelpClientSize);

        dialog.AddHeading("TypeyTypey", $"Version {VersionInfo.Display}");

        dialog.AddSection("What it does", card => card.Add(dialog.Paragraph(
            "TypeyTypey types text one simulated keystroke at a time instead of pasting it. Remote " +
            "desktops, KVM and IPMI consoles and some credential prompts accept typing but ignore " +
            "Ctrl+V, because they never share your clipboard. These characters arrive as ordinary " +
            "Unicode key events, so those targets see a keyboard.")));

        dialog.AddSection("Using it", card =>
        {
            card.Add(dialog.Paragraph(
                "Copy something, focus the window you want it in, then press the type hotkey. To send " +
                "an older entry, open the history picker and choose one to arm it, then press the type " +
                "hotkey where you want it."));
            card.Add(dialog.Definition(settings.TypeClipboardHotkey.ToString(), "Type the clipboard, or the armed entry."));
            card.Add(dialog.Definition(settings.HistoryHotkey.ToString(), "Open the clipboard history picker."));
            card.Add(dialog.Definition(settings.StopTypingHotkey.ToString(), "Stop a typing run already under way."));
            card.Add(UiKit.Caption("These are the hotkeys currently configured. Change them in Settings."));
        });

        dialog.AddSection("Typing mode", card =>
        {
            card.Add(dialog.Paragraph(
                "How the text reaches the target. Ordinary Windows applications accept either; a remote " +
                "console running in a browser usually needs real key presses."));
            foreach (TypingMode mode in TypingModeText.InDisplayOrder)
                card.Add(dialog.Definition(TypingModeText.Label(mode), TypingModeText.Description(mode)));
            card.Add(dialog.Definition("Now using", TypingModeText.Label(settings.TypingMode)));
            card.Add(UiKit.Caption(
                "Physical keypresses are mapped through the keyboard layout of the window you are typing into. " +
                "A remote console set to a different layout can still produce different characters, and TypeyTypey " +
                "cannot see that from this machine."));

            foreach ((TypingMode mode, HotkeyBinding binding) in settings.AssignedModeOverrides())
                card.Add(dialog.Definition(binding.ToString(), $"Type once using {TypingModeText.Label(mode)}."));
        });

        foreach (HelpTopic topic in HelpTopics.Operational)
            dialog.AddSection(topic.Title, card =>
            {
                foreach (string paragraph in topic.Paragraphs)
                    card.Add(dialog.Paragraph(paragraph));
            });

        dialog.AddSection("Command line", card =>
        {
            foreach (CommandLineOption option in CommandLine.Options)
                card.Add(dialog.Definition(option.Flag, option.Summary));
            card.Add(UiKit.Caption(
                "These hand the command to the instance already running rather than starting a second " +
                "one. --help and --admintask are the exceptions: they act and exit."));
        });

        dialog.AddSection("Elevation", card => card.Add(dialog.Paragraph(
            "Windows will not let a program send keystrokes to a window with more privilege than it " +
            "has, so typing into an elevated application needs an elevated TypeyTypey. Turn on Run as " +
            "administrator in Settings, use --admin for one session, or use --admintask to start it " +
            "elevated at sign-in without a prompt. The tray tooltip then reads TypeyTypey " +
            "(Administrator).")));

        dialog.AddSection("What it never does", card =>
        {
            card.Add(dialog.Paragraph(
                "Clipboard text is never written to disk, logged, sent anywhere, or shown in an error " +
                "message. History is held in memory only and discarded on exit. No network activity, " +
                "no telemetry."));
            card.Add(UiKit.Caption("TypeyTypey is not a password manager. Nothing it holds survives the process."));
        });

        dialog.Finish();
        dialog.ShowDialog(owner);
    }

    public static void ShowAbout(IWin32Window? owner, AppTheme theme, Icon? icon)
    {
        using var dialog = new InfoDialog("About TypeyTypey", icon, theme, WindowPlacement.DefaultAboutClientSize);

        dialog.AddHeading(VersionInfo.Product, $"Version {VersionInfo.Display}");
        dialog.AddSection("Details", card =>
        {
            if (VersionInfo.Description.Length > 0)
                card.Add(dialog.Definition("Description", VersionInfo.Description));
            if (VersionInfo.Company.Length > 0)
                card.Add(dialog.Definition("Author", VersionInfo.Company));
            card.Add(dialog.Definition("Version", VersionInfo.Display));
            card.Add(dialog.Definition("Licence", "MIT"));
            if (VersionInfo.Copyright.Length > 0)
                card.Add(UiKit.Caption(VersionInfo.Copyright));
        });

        dialog.Finish();
        dialog.ShowDialog(owner);
    }

    // ---------- building blocks ----------

    private void AddHeading(string title, string subtitle)
    {
        var stack = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        stack.Controls.Add(new Label { Text = title, Font = UiKit.Title, AutoSize = true, Margin = new Padding(0) });
        stack.Controls.Add(new MutedLabel
        {
            Text = subtitle,
            Font = UiKit.Helper,
            AutoSize = true,
            Margin = new Padding(0, UiKit.SpaceTight, 0, 0)
        });
        _page.Controls.Add(stack);
    }

    private void AddSection(string eyebrow, Action<CardBuilder> build)
    {
        _page.Controls.Add(UiKit.Eyebrow(eyebrow));

        var card = new CardPanel { Dock = DockStyle.Top };
        var content = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        build(new CardBuilder(content));
        card.Controls.Add(content);
        _page.Controls.Add(card);
    }

    private Label Paragraph(string text)
    {
        var label = new Label
        {
            Text = text,
            Font = UiKit.Body,
            AutoSize = true,
            MaximumSize = new Size(WindowPlacement.DefaultHelpClientSize.Width - 90, 0),
            Margin = new Padding(0, 0, 0, UiKit.Space)
        };
        _wrapping.Add(label);
        return label;
    }

    /// <summary>A term and its explanation. The term is monospaced so flags and hotkeys line up.</summary>
    private Control Definition(string term, string explanation)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, UiKit.SpaceTight)
        };
        var caption = new Label
        {
            Text = term,
            Font = UiKit.Mono,
            AutoSize = false,
            AutoEllipsis = false,
            Width = 148,
            Margin = new Padding(0, 1, UiKit.SpaceWide, 0)
        };
        _terms.Add(caption);
        row.Controls.Add(caption);

        var detail = new Label
        {
            Text = explanation,
            Font = UiKit.Body,
            AutoSize = true,
            MaximumSize = new Size(WindowPlacement.DefaultHelpClientSize.Width - 250, 0),
            Margin = new Padding(0, 1, 0, 0)
        };
        _wrapping.Add(detail);
        row.Controls.Add(detail);
        return row;
    }

    /// <summary>Narrow wrapper so a section's content reads as a list of additions.</summary>
    private sealed class CardBuilder(FlowLayoutPanel content)
    {
        public void Add(Control control) => content.Controls.Add(control);
    }
}
