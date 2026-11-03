using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using ERP.Infrastructure.Import;
using Microsoft.Data.SqlClient;

namespace ERP.EasyRetailExporter;

/// <summary>
/// The exporter's window: three steps down one page - connect to the SQL Server, choose the
/// database and where the file goes, export - each opening once the one above it is done,
/// with every section of the export ticked off as it is read.
/// </summary>
/// <remarks>
/// It was a console that asked one question at a time, in a black window. A shop's
/// computer is where it runs, and the person running it is rarely the one who
/// installed SQL Server: so the window finds the servers on the computer itself, lists the
/// EasyRetail databases rather than asking for a name, remembers what it was given for next
/// time, and says in plain words what went wrong when something does.
/// </remarks>
internal sealed class ExportForm : Form
{
    private const string DefaultServer = @".\SQLEXPRESS";

    /// <summary>What each section of the file is called on screen.</summary>
    private static readonly Dictionary<string, string> SectionLabels = new(StringComparer.Ordinal)
    {
        ["company"] = "Company",
        ["groups"] = "Account groups",
        ["ledgers"] = "Ledgers",
        ["customers"] = "Customers",
        ["suppliers"] = "Suppliers",
        ["employees"] = "Employees",
        ["categories"] = "Categories",
        ["subClasses"] = "Sub-classes",
        ["brands"] = "Brands",
        ["units"] = "Units",
        ["warehouses"] = "Warehouses",
        ["products"] = "Products",
        ["barcodes"] = "Barcodes",
        ["stock"] = "Stock",
    };

    private readonly IExportBackend _backend;
    private readonly Remembered _remembered;
    private readonly IReadOnlyList<string> _localServers;
    private readonly string[] _sectionOrder = [.. EasyRetailQueries.Sections.Select(section => section.Name)];
    private readonly Dictionary<string, SectionLine> _sections = new(StringComparer.Ordinal);

    private readonly Font _titleFont = new("Segoe UI Semibold", 15F);
    private readonly Font _headingFont = new("Segoe UI Semibold", 11F);
    private readonly Font _strongFont = new("Segoe UI Semibold", 9.75F);
    private readonly Font _smallFont = new("Segoe UI", 8.75F);
    private readonly Icon? _icon = LoadIcon();
    private readonly ToolTip _details = new() { AutoPopDelay = 30000 };

    private readonly StepBadge _connectStep = new(1);
    private readonly StepBadge _chooseStep = new(2);
    private readonly StepBadge _exportStep = new(3);

    private readonly ComboBox _server = new() { DropDownStyle = ComboBoxStyle.DropDown, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly Label _serverHint = new() { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, ForeColor = Palette.Muted };
    private readonly RadioButton _windowsSignIn = new() { Text = "Windows account", AutoSize = true, Checked = true, Margin = new Padding(0, 3, 18, 3) };
    private readonly RadioButton _sqlSignIn = new() { Text = "SQL Server login", AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
    private readonly TextBox _user = new() { Width = 170, Margin = new Padding(0, 0, 14, 0) };
    private readonly TextBox _password = new() { Width = 170, UseSystemPasswordChar = true, Margin = Padding.Empty };
    private readonly ActionButton _connect = new("Connect");
    private readonly Label _connectStatus = new() { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(12, 0, 0, 0) };

    private readonly ComboBox _database = new() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly DateTimePicker _asAt = new() { Format = DateTimePickerFormat.Short, Width = 150, Margin = new Padding(0, 0, 12, 0) };
    private readonly TextBox _output = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 8, 0) };
    private readonly ActionButton _browse = new("Browse…", compact: true);

    private readonly ProgressBar _progress = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 10, Style = ProgressBarStyle.Continuous };
    private readonly Label _progressText = new() { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, ForeColor = Palette.Muted };
    private readonly TableLayoutPanel _result = new();
    private readonly Label _resultText = new() { AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, ForeColor = Palette.Ink };
    private readonly ActionButton _showFile = new("Show in folder", compact: true);
    private readonly ActionButton _copyPath = new("Copy path", compact: true);

    private readonly Label _footerText = new() { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Palette.Muted };
    private readonly ActionButton _export = new("Export");
    private readonly ActionButton _close = new("Close");

    private readonly TableLayoutPanel _header;
    private readonly TableLayoutPanel _footer;

    private Panel? _body;
    private TableLayoutPanel? _steps;
    private Control? _loginRow;
    private Control? _loginCaption;

    /// <summary>The sign-in the database list came from; null until one connects.</summary>
    private ServerSignIn? _signedIn;
    private bool _connecting;

    /// <summary>The export under way, to stop it with; null when none is.</summary>
    private CancellationTokenSource? _running;

    /// <summary>The file the save dialog already asked about replacing.</summary>
    private string? _confirmedReplace;

    /// <summary>The file the last export saved.</summary>
    private string? _saved;
    private bool _closeWhenStopped;

    /// <summary>Builds the window.</summary>
    /// <param name="backend">The SQL Server to read.</param>
    /// <param name="remembered">What the last run was given.</param>
    /// <param name="localServers">The SQL Server instances on this computer.</param>
    public ExportForm(IExportBackend backend, Remembered remembered, IReadOnlyList<string> localServers)
    {
        _backend = backend;
        _remembered = remembered;
        _localServers = localServers;

        SuspendLayout();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "EasyRetail export";
        Icon = _icon ?? Icon;
        ShowIcon = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(660, 700);
        MinimumSize = new Size(580, 520);
        BackColor = Color.White;
        ForeColor = Palette.Ink;

        _connect.Font = _strongFont;
        _export.Font = _strongFont;
        _close.Font = _strongFont;
        _export.SetPrimary(true);
        _serverHint.Font = _smallFont;
        _progressText.Font = _smallFont;

        // The part that fills is added first: docking works from the last control added.
        _header = BuildHeader();
        _footer = BuildFooter();
        Controls.Add(BuildBody());
        Controls.Add(_header);
        Controls.Add(_footer);

        AcceptButton = _connect;
        CancelButton = _close;

        ResumeLayout(performLayout: true);

        FillIn();
        Wire();
        SetStatus(Palette.Muted, string.Empty);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToScreen(centre: true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _server.Focus();
        _server.SelectAll();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running is not null)
        {
            e.Cancel = true;

            DialogResult answer = MessageBox.Show(
                this,
                "The export is still running. Stop it and close? Nothing will be saved.",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer == DialogResult.Yes)
            {
                _closeWhenStopped = true;
                _running.Cancel();
            }
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _headingFont.Dispose();
            _strongFont.Dispose();
            _smallFont.Dispose();
            _icon?.Dispose();
            _details.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// As tall as the three steps need, so nothing scrolls - but no taller than the screen,
    /// where a laptop's leaves less: then the steps scroll, and the buttons stay in view.
    /// </summary>
    /// <param name="centre">Whether to centre the window on the screen, as when it opens; otherwise it grows from where it is.</param>
    private void FitToScreen(bool centre)
    {
        if (_steps is null)
        {
            return;
        }

        // Measured once laid out, with a panel just shown counted in.
        _body?.PerformLayout();

        Rectangle screen = Screen.FromControl(this).WorkingArea;
        int frame = Height - ClientSize.Height;
        int wanted = _header.Height + _steps.Height + _footer.Height + LogicalToDeviceUnits(40);

        // Opening, it takes what the steps need; later it only grows, never jumps smaller.
        int height = centre ? wanted : Math.Max(wanted, ClientSize.Height);
        ClientSize = new Size(ClientSize.Width, Math.Min(height, screen.Height - frame));

        int top = centre ? screen.Top + Math.Max(0, (screen.Height - Height) / 2) : Top;
        Location = new Point(
            centre ? screen.Left + Math.Max(0, (screen.Width - Width) / 2) : Left,
            Math.Max(screen.Top, Math.Min(top, screen.Bottom - Height)));
    }

    /// <summary>The window's icon, from the program itself; none where it cannot be read.</summary>
    private static Icon? LoadIcon()
    {
        using Stream? stream = typeof(ExportForm).Assembly.GetManifestResourceStream("ERP.EasyRetailExporter.exporter.ico");

        return stream is null ? null : new Icon(stream);
    }

    /// <summary>The failures a sign-in or an export can meet - as against a fault in this program.</summary>
    private static bool Expected(Exception exception) =>
        exception is DbException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException
            or Win32Exception;

    /// <summary>
    /// What went wrong, in the words of the person at the computer rather than of the
    /// driver; the driver's own words go in the tooltip, for whoever supports them.
    /// </summary>
    private static string Explain(Exception exception, string server) => exception switch
    {
        SqlException { Number: 18456 } => "The login or the password was not accepted.",
        SqlException { Number: 4060 } => "That login cannot open the database.",

        // Class 20 and over is a connection that never opened; the number says how, and
        // differs between the network libraries, so the class decides.
        SqlException sql when sql.Class >= 20 || sql.Number is -1 or 2 or 26 or 40 or 53 =>
            $@"No SQL Server answered at {server}. Check the name - for example .\SQLEXPRESS - and that SQL Server is running.",
        UnauthorizedAccessException => "Windows did not allow the file to be saved there. Choose another folder.",
        _ => FirstLine(exception.Message),
    };

    /// <summary>What a section of the file is called on screen.</summary>
    private static string LabelOf(string section) =>
        SectionLabels.GetValueOrDefault(section) ?? char.ToUpperInvariant(section[0]) + section[1..];

    private static string FirstLine(string message)
    {
        int end = message.IndexOfAny(['\r', '\n']);

        return end < 0 ? message : message[..end];
    }

    private static string SizeOf(long bytes) => bytes switch
    {
        >= 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024.0):0.0} MB"),
        >= 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0} KB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{bytes} bytes"),
    };

    private static string Took(TimeSpan elapsed) => elapsed.TotalSeconds < 60
        ? string.Create(CultureInfo.CurrentCulture, $"{Math.Max(1, (int)Math.Round(elapsed.TotalSeconds))} s")
        : string.Create(CultureInfo.CurrentCulture, $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s");

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 7, 10, 7),
    };

    private static TableLayoutPanel Stack(int columns = 1)
    {
        TableLayoutPanel stack = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = columns,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        for (int column = 0; column < columns; column++)
        {
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
        }

        return stack;
    }

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        FlowLayoutPanel flow = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left,
            WrapContents = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        flow.Controls.AddRange(controls);
        return flow;
    }

    /// <summary>The fields of a step: captions in a column of their own, each beside its box.</summary>
    private static TableLayoutPanel Fields()
    {
        TableLayoutPanel grid = Stack(2);
        grid.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 112F);
        grid.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100F);
        return grid;
    }

    /// <summary>Adds a field to a step, with its caption beside it.</summary>
    /// <returns>The caption, or null for a field without one.</returns>
    private static Label? Row(TableLayoutPanel grid, string? caption, Control field)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label? label = caption is null ? null : Caption(caption);

        if (label is not null)
        {
            grid.Controls.Add(label, 0, row);
        }

        field.Margin = new Padding(field.Margin.Left, 3, field.Margin.Right, 3);
        grid.Controls.Add(field, 1, row);

        return label;
    }

    private TableLayoutPanel BuildHeader()
    {
        TableLayoutPanel header = Stack();
        header.Dock = DockStyle.Top;
        header.BackColor = Palette.Brand;
        header.Padding = new Padding(24, 16, 24, 16);

        header.Controls.Add(new Label
        {
            Text = "EasyRetail export",
            Font = _titleFont,
            ForeColor = Color.White,
            AutoSize = true,
            Margin = Padding.Empty,
        });
        header.Controls.Add(new Label
        {
            Text = "Saves EasyRetail's data to one file for Settings › Import data. Nothing in EasyRetail is changed.",
            ForeColor = Palette.BrandSoft,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 4, 0, 0),
        });

        return header;
    }

    private Panel BuildBody()
    {
        Panel body = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.White,
            Padding = new Padding(20, 18, 24, 6),
        };
        _body = body;

        TableLayoutPanel steps = Stack();
        steps.Dock = DockStyle.Top;
        _steps = steps;

        // The login appearing, a longer message, the saved file's panel: the window grows
        // to keep them in view, as far as the screen allows.
        steps.SizeChanged += (_, _) =>
        {
            if (Visible)
            {
                FitToScreen(centre: false);
            }
        };

        steps.Controls.Add(Step(
            _connectStep,
            "Connect to SQL Server",
            "The server EasyRetail keeps its data on - usually this computer.",
            BuildConnect()));
        steps.Controls.Add(Step(
            _chooseStep,
            "Choose what to export",
            "The EasyRetail database, the date its balances are taken at, and where the file goes.",
            BuildChoose()));
        steps.Controls.Add(Step(
            _exportStep,
            "Export",
            "Every section is read into the one file. A large database takes a few minutes.",
            BuildExport()));

        body.Controls.Add(steps);
        return body;
    }

    private TableLayoutPanel Step(StepBadge badge, string title, string description, Control content)
    {
        TableLayoutPanel step = Stack(2);
        step.ColumnStyles[0] = new ColumnStyle(SizeType.AutoSize);
        step.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100F);
        step.Margin = new Padding(0, 0, 0, 12);
        step.RowCount = 3;

        badge.Font = _strongFont;
        badge.Margin = new Padding(0, 0, 12, 0);
        step.Controls.Add(badge, 0, 0);
        step.SetRowSpan(badge, 2);

        step.Controls.Add(
            new Label { Text = title, Font = _headingFont, AutoSize = true, Margin = new Padding(0, 3, 0, 0) },
            1,
            0);
        step.Controls.Add(
            new Label
            {
                Text = description,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Palette.Muted,
                Margin = new Padding(0, 1, 0, 6),
            },
            1,
            1);
        step.Controls.Add(content, 1, 2);

        return step;
    }

    private TableLayoutPanel BuildConnect()
    {
        TableLayoutPanel grid = Fields();

        Row(grid, "Server", _server);
        Row(grid, null, _serverHint);
        Row(grid, "Sign in with", Flow(_windowsSignIn, _sqlSignIn));

        Label password = Caption("Password");
        password.Margin = new Padding(0, 4, 8, 4);
        _loginRow = Flow(_user, password, _password);
        _loginCaption = Row(grid, "Login", _loginRow);

        TableLayoutPanel connect = Stack(2);
        connect.ColumnStyles[0] = new ColumnStyle(SizeType.AutoSize);
        connect.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100F);
        _connect.Margin = Padding.Empty;
        connect.Controls.Add(_connect, 0, 0);
        connect.Controls.Add(_connectStatus, 1, 0);
        Row(grid, null, connect);

        return grid;
    }

    private TableLayoutPanel BuildChoose()
    {
        TableLayoutPanel grid = Fields();

        Row(grid, "Database", _database);

        Label asAtHint = new()
        {
            Text = "Opening balances and stock are read as at this date.",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Palette.Muted,
            Font = _smallFont,
            Margin = new Padding(0, 6, 0, 0),
        };
        Row(grid, "Balances as at", Flow(_asAt, asAtHint));

        TableLayoutPanel saveTo = Stack(2);
        saveTo.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100F);
        saveTo.ColumnStyles[1] = new ColumnStyle(SizeType.AutoSize);
        _browse.Margin = Padding.Empty;
        _browse.Anchor = AnchorStyles.Left;
        saveTo.Controls.Add(_output, 0, 0);
        saveTo.Controls.Add(_browse, 1, 0);
        Row(grid, "Save to", saveTo);

        return grid;
    }

    private TableLayoutPanel BuildExport()
    {
        TableLayoutPanel area = Stack();

        _progress.Margin = new Padding(0, 4, 0, 6);
        _progress.Maximum = _sectionOrder.Length;
        area.Controls.Add(_progress);
        _progressText.Margin = new Padding(0, 0, 0, 8);
        area.Controls.Add(_progressText);

        TableLayoutPanel sections = Stack(3);

        foreach (string name in _sectionOrder)
        {
            SectionLine line = new(LabelOf(name));
            _sections[name] = line;
            sections.Controls.Add(line);
        }

        area.Controls.Add(sections);

        _result.AutoSize = true;
        _result.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _result.Dock = DockStyle.Fill;
        _result.ColumnCount = 3;
        _result.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _result.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _result.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _result.BackColor = Palette.GoodSoft;
        _result.Padding = new Padding(12, 10, 12, 10);
        _result.Margin = new Padding(0, 10, 0, 0);
        _result.Visible = false;
        _resultText.Margin = new Padding(0, 0, 12, 0);
        _showFile.Margin = new Padding(0, 0, 8, 0);
        _copyPath.Margin = Padding.Empty;
        _result.Controls.Add(_resultText, 0, 0);
        _result.Controls.Add(_showFile, 1, 0);
        _result.Controls.Add(_copyPath, 2, 0);
        area.Controls.Add(_result);

        return area;
    }

    private TableLayoutPanel BuildFooter()
    {
        TableLayoutPanel footer = Stack(3);
        footer.Dock = DockStyle.Bottom;
        footer.BackColor = Palette.Canvas;
        footer.Padding = new Padding(24, 12, 24, 12);
        footer.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100F);
        footer.ColumnStyles[1] = new ColumnStyle(SizeType.AutoSize);
        footer.ColumnStyles[2] = new ColumnStyle(SizeType.AutoSize);

        _export.Margin = new Padding(12, 0, 8, 0);
        _close.Margin = Padding.Empty;
        footer.Controls.Add(_footerText, 0, 0);
        footer.Controls.Add(_export, 1, 0);
        footer.Controls.Add(_close, 2, 0);

        // A hairline over the bar, as the ERP draws over its own.
        footer.Paint += (_, paint) =>
        {
            using Pen line = new(Palette.Line);
            paint.Graphics.DrawLine(line, 0, 0, footer.Width, 0);
        };

        return footer;
    }

    /// <summary>Fills the window in: what was used last time, or what this computer suggests.</summary>
    private void FillIn()
    {
        foreach (string server in _localServers)
        {
            _server.Items.Add(server);
        }

        _server.Text = _remembered.Server ?? (_localServers.Count > 0 ? _localServers[0] : DefaultServer);
        _serverHint.Text = _localServers.Count > 0
            ? $"Found on this computer: {string.Join(", ", _localServers)}"
            : @"None found on this computer. Type its name, for example .\SQLEXPRESS or SHOP-PC\SQLEXPRESS.";

        if (!string.IsNullOrEmpty(_remembered.User))
        {
            _sqlSignIn.Checked = true;
            _user.Text = _remembered.User;
        }

        _asAt.Value = DateTime.Today;

        string folder = _remembered.Folder is { Length: > 0 } remembered && Directory.Exists(remembered)
            ? remembered
            : ExportFile.DefaultFolder();
        _output.Text = Path.Combine(folder, ExportFile.Name);

        _progressText.Text = "Each section is ticked off here as it is read.";
        ShowSections(Mark.Waiting);
    }

    private void Wire()
    {
        _server.TextChanged += OnSignInChanged;
        _user.TextChanged += OnSignInChanged;
        _password.TextChanged += OnSignInChanged;
        _windowsSignIn.CheckedChanged += OnSignInChanged;

        _connect.Click += OnConnect;
        _database.SelectedIndexChanged += (_, _) => UpdateControls();
        _output.TextChanged += (_, _) =>
        {
            _confirmedReplace = null;
            UpdateControls();
        };
        _browse.Click += OnBrowse;
        _export.Click += OnExport;
        _close.Click += OnClose;
        _showFile.Click += (_, _) => Explorer.Select(_saved);
        _copyPath.Click += (_, _) =>
        {
            if (_saved is not null)
            {
                Clipboard.SetText(_saved);
                _copyPath.Text = "Copied";
            }
        };
    }

    /// <summary>A changed server or sign-in no longer matches the databases listed: they go until it connects again.</summary>
    private void OnSignInChanged(object? sender, EventArgs e)
    {
        if (_signedIn is not null)
        {
            _signedIn = null;
            _database.Items.Clear();
            SetStatus(Palette.Muted, "Changed. Connect again to list the databases.");
        }
        else
        {
            UpdateControls();
        }

        // Choosing a login is choosing to type one.
        if (sender == _windowsSignIn && _sqlSignIn.Checked)
        {
            _user.Focus();
        }
    }

    private async void OnConnect(object? sender, EventArgs e)
    {
        string server = _server.Text.Trim();
        string? user = _sqlSignIn.Checked ? _user.Text.Trim() : null;

        if (server.Length == 0)
        {
            SetStatus(Palette.Bad, "Type the server's name first.");
            _server.Focus();
            return;
        }

        if (user is { Length: 0 })
        {
            SetStatus(Palette.Bad, "Type the SQL Server login, or sign in with the Windows account.");
            _user.Focus();
            return;
        }

        ServerSignIn signIn = new(server, user, _password.Text);

        _connecting = true;
        _signedIn = null;
        _database.Items.Clear();
        UseWaitCursor = true;
        SetStatus(Palette.Muted, $"Connecting to {server}…");

        try
        {
            IReadOnlyList<DatabaseChoice> databases =
                await Task.Run(() => _backend.ListDatabasesAsync(signIn, CancellationToken.None), CancellationToken.None);

            _signedIn = signIn;
            Offer(databases);
        }
        catch (Exception exception) when (Expected(exception))
        {
            SetStatus(Palette.Bad, Explain(exception, server));
            _details.SetToolTip(_connectStatus, exception.Message);
        }
        finally
        {
            _connecting = false;
            UseWaitCursor = false;
            UpdateControls();
        }

        // Only once the boxes are enabled again: while they were not, Windows moved the
        // cursor to the one button left enabled, Close, and the next Enter pressed it.
        if (_signedIn is null)
        {
            _server.Focus();
        }
        else
        {
            _database.Focus();
        }
    }

    /// <summary>Lists the databases, the EasyRetail ones first, and picks the one last exported or the first of them.</summary>
    private void Offer(IReadOnlyList<DatabaseChoice> databases)
    {
        foreach (DatabaseChoice database in databases.OrderByDescending(database => database.EasyRetail))
        {
            _database.Items.Add(new DatabaseItem(database));
        }

        DatabaseItem? wanted = _database.Items.OfType<DatabaseItem>()
            .FirstOrDefault(item => string.Equals(item.Choice.Name, _remembered.Database, StringComparison.OrdinalIgnoreCase))
            ?? _database.Items.OfType<DatabaseItem>().FirstOrDefault();

        if (wanted is not null)
        {
            _database.SelectedItem = wanted;
        }

        int recognised = databases.Count(database => database.EasyRetail);

        if (recognised > 0)
        {
            SetStatus(
                Palette.Good,
                recognised == 1 ? "Connected. 1 EasyRetail database found." : $"Connected. {recognised} EasyRetail databases found.");
        }
        else
        {
            SetStatus(
                Palette.Warn,
                databases.Count == 0
                    ? "Connected, but this sign-in can open no databases there."
                    : "Connected, but no EasyRetail database was recognised. Choose the right one in the list.");
        }
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        string current = _output.Text.Trim();

        using SaveFileDialog dialog = new()
        {
            Title = "Save the export as",
            Filter = "Export file (*.json)|*.json|All files (*.*)|*.*",
            FileName = Path.GetFileName(current) is { Length: > 0 } name ? name : ExportFile.Name,
            InitialDirectory = Path.GetDirectoryName(current) is { Length: > 0 } folder && Directory.Exists(folder)
                ? folder
                : ExportFile.DefaultFolder(),
            OverwritePrompt = true,
            AddExtension = true,
            DefaultExt = "json",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _output.Text = dialog.FileName;

            // Asked about by the dialog already, so not asked again on export.
            _confirmedReplace = dialog.FileName;
        }
    }

    private async void OnExport(object? sender, EventArgs e)
    {
        if (_signedIn is not { } signIn || _database.SelectedItem is not DatabaseItem chosen)
        {
            return;
        }

        string? output = Destination();

        if (output is null)
        {
            return;
        }

        using CancellationTokenSource running = new();
        _running = running;
        _saved = null;
        _result.Visible = false;
        _copyPath.Text = "Copy path";
        _progress.Value = 0;
        ShowSections(Mark.Waiting);
        _sections[_sectionOrder[0]].SetMark(Mark.Working, "reading…");
        _progressText.ForeColor = Palette.Muted;
        _progressText.Text = $"Reading {chosen.Choice.Name}…";
        UpdateControls();

        Progress<(string Section, int Rows)> progress = new(read => OnSectionRead(read.Section, read.Rows));
        DateOnly asAt = DateOnly.FromDateTime(_asAt.Value);
        Stopwatch clock = Stopwatch.StartNew();

        try
        {
            // Off the window's thread, so it stays responsive while a large database is read.
            await Task.Run(
                () => _backend.ExportAsync(
                    signIn,
                    chosen.Choice.Name,
                    asAt,
                    output,
                    (section, rows) => ((IProgress<(string, int)>)progress).Report((section, rows)),
                    running.Token),
                running.Token);

            Saved(output, chosen.Choice.Name, clock.Elapsed);
            new Remembered(signIn.Server, signIn.User, chosen.Choice.Name, Path.GetDirectoryName(output)).Save();
        }
        catch (Exception exception) when (running.IsCancellationRequested
            && (exception is OperationCanceledException || Expected(exception)))
        {
            // Stopped - which SQL Server can report as a failed command rather than a cancellation.
            ShowSections(Mark.Waiting);
            _progress.Value = 0;
            _progressText.ForeColor = Palette.Warn;
            _progressText.Text = "Stopped. Nothing was saved.";
        }
        catch (Exception exception) when (Expected(exception))
        {
            _sections.Values.FirstOrDefault(line => line.Reading)?.SetMark(Mark.Failed, "failed");
            _progressText.ForeColor = Palette.Bad;
            _progressText.Text = $"The export stopped: {Explain(exception, signIn.Server)}";
            _details.SetToolTip(_progressText, exception.Message);
        }
        finally
        {
            _running = null;
            UpdateControls();
        }

        if (_closeWhenStopped)
        {
            Close();
            return;
        }

        // Back from the Stop button, the only one enabled while the export ran.
        if (_saved is not null)
        {
            _showFile.Focus();
        }
        else
        {
            _export.Focus();
        }
    }

    /// <summary>The file to save, checked: a folder typed in gets the file's own name, and a file already there is asked about.</summary>
    private string? Destination()
    {
        string output;

        try
        {
            output = Path.GetFullPath(_output.Text.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            SetStatus(Palette.Bad, "That is not a file name. Choose where to save with Browse.");
            return null;
        }

        if (Directory.Exists(output))
        {
            output = Path.Combine(output, ExportFile.Name);
            _output.Text = output;
        }

        string folder = Path.GetDirectoryName(output) ?? string.Empty;

        if (!Directory.Exists(folder))
        {
            SetStatus(Palette.Bad, $"The folder {folder} does not exist. Choose where to save with Browse.");
            return null;
        }

        if (File.Exists(output)
            && !string.Equals(output, _confirmedReplace, StringComparison.OrdinalIgnoreCase)
            && MessageBox.Show(
                this,
                $"{Path.GetFileName(output)} is already in {folder}. Replace it?",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return null;
        }

        return output;
    }

    private void OnSectionRead(string section, int rows)
    {
        if (_sections.TryGetValue(section, out SectionLine? line))
        {
            line.SetMark(Mark.Done, rows.ToString("N0", CultureInfo.CurrentCulture));
        }

        int done = Array.IndexOf(_sectionOrder, section) + 1;
        _progress.Value = Math.Clamp(done, 0, _progress.Maximum);

        if (done > 0 && done < _sectionOrder.Length)
        {
            string next = _sectionOrder[done];
            _sections[next].SetMark(Mark.Working, "reading…");
            _progressText.Text = $"Reading {LabelOf(next).ToLower(CultureInfo.CurrentCulture)}… {done} of {_sectionOrder.Length} sections read.";
        }
    }

    private void Saved(string output, string database, TimeSpan took)
    {
        _saved = output;
        _progress.Value = _progress.Maximum;
        _progressText.ForeColor = Palette.Good;
        _progressText.Text = $"All {_sectionOrder.Length} sections of {database} read in {Took(took)}.";

        long size = new FileInfo(output).Length;
        _resultText.Text = $"Saved {Path.GetFileName(output)} ({SizeOf(size)}) in {Path.GetDirectoryName(output)}."
            + Environment.NewLine
            + "In the ERP, open Settings › Import data, choose Export file, and pick this file.";
        _result.Visible = true;

        // The window grows by the panel where the screen has room; where it has not, the
        // steps scroll to it.
        _body?.ScrollControlIntoView(_result);
    }

    private void ShowSections(Mark mark)
    {
        foreach (SectionLine line in _sections.Values)
        {
            line.SetMark(mark, string.Empty);
        }
    }

    private void OnClose(object? sender, EventArgs e)
    {
        if (_running is null)
        {
            Close();
            return;
        }

        // Stop is where the cursor rests while the export runs, so an Enter pressed out of
        // habit is asked about rather than throwing the export away.
        DialogResult answer = MessageBox.Show(
            this,
            "Stop the export? Nothing will be saved.",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (answer == DialogResult.Yes && _running is not null)
        {
            _running.Cancel();
            _progressText.Text = "Stopping…";
        }
    }

    /// <summary>Says how the connection stands, under the Connect button, and brings the rest in line.</summary>
    private void SetStatus(Color tone, string status)
    {
        _connectStatus.ForeColor = tone;
        _connectStatus.Text = status;
        _details.SetToolTip(_connectStatus, null);
        UpdateControls();
    }

    /// <summary>Brings every control in line with where the export stands.</summary>
    private void UpdateControls()
    {
        bool running = _running is not null;
        bool connected = _signedIn is not null;
        bool chosen = _database.SelectedItem is DatabaseItem;
        bool idle = !running && !_connecting;

        _server.Enabled = idle;
        _windowsSignIn.Enabled = idle;
        _sqlSignIn.Enabled = idle;
        _user.Enabled = idle;
        _password.Enabled = idle;

        // Asked for only when it is a login that signs in. Set every time: a control reads
        // as hidden until the window is shown, so comparing first would leave it showing.
        if (_loginRow is not null && _loginCaption is not null)
        {
            _loginRow.Visible = _sqlSignIn.Checked;
            _loginCaption.Visible = _sqlSignIn.Checked;
        }

        _connect.Enabled = idle;
        _connect.Text = connected ? "Connect again" : "Connect";
        _connect.SetPrimary(!connected);

        _database.Enabled = connected && idle;
        _asAt.Enabled = connected && idle;
        _output.Enabled = connected && idle;
        _browse.Enabled = connected && idle;

        _export.Enabled = connected && chosen && idle && _output.Text.Trim().Length > 0;
        _export.Text = _saved is null ? "Export" : "Export again";
        _close.Text = running ? "Stop" : "Close";
        AcceptButton = connected ? _export : _connect;

        _connectStep.SetMark(connected ? Mark.Done : Mark.Working);
        _chooseStep.SetMark(StepMark(reached: connected, done: chosen));
        _exportStep.SetMark(StepMark(reached: connected && chosen, done: _saved is not null));
        _footerText.Text = Guidance(running, connected, chosen);
    }

    /// <summary>How a step stands: waiting for the one above it, the one to take, or done.</summary>
    private static Mark StepMark(bool reached, bool done)
    {
        if (done)
        {
            return Mark.Done;
        }

        return reached ? Mark.Working : Mark.Waiting;
    }

    /// <summary>What to do next, along the foot of the window.</summary>
    private string Guidance(bool running, bool connected, bool chosen)
    {
        if (running)
        {
            return "Exporting. Keep this window open until it is done.";
        }

        if (_saved is not null)
        {
            return "Saved. The file is ready for Settings › Import data.";
        }

        if (!connected)
        {
            return "Step 1 of 3: connect to the SQL Server.";
        }

        return _database.SelectedItem is DatabaseItem item && chosen
            ? $"Ready to export {item.Choice.Name}."
            : "Step 2 of 3: choose the database.";
    }

    /// <summary>A database in the list: its name, and a note where it does not look like EasyRetail's.</summary>
    private sealed record DatabaseItem(DatabaseChoice Choice)
    {
        public override string ToString() => Choice.EasyRetail ? Choice.Name : $"{Choice.Name}   (not recognised as EasyRetail)";
    }
}
