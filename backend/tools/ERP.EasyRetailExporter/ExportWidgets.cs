using System.Drawing.Drawing2D;

namespace ERP.EasyRetailExporter;

/// <summary>The colours the window is drawn in: the ERP's own, so the two read as one product.</summary>
internal static class Palette
{
    public static Color Brand { get; } = Color.FromArgb(79, 70, 229);

    public static Color BrandHover { get; } = Color.FromArgb(67, 56, 202);

    public static Color BrandPressed { get; } = Color.FromArgb(55, 48, 163);

    /// <summary>The main button while it cannot be pressed yet.</summary>
    public static Color BrandMuted { get; } = Color.FromArgb(165, 180, 252);

    public static Color BrandSoft { get; } = Color.FromArgb(224, 231, 255);

    public static Color Ink { get; } = Color.FromArgb(15, 23, 42);

    public static Color Muted { get; } = Color.FromArgb(100, 116, 139);

    public static Color Subtle { get; } = Color.FromArgb(148, 163, 184);

    public static Color Line { get; } = Color.FromArgb(203, 213, 225);

    public static Color Locked { get; } = Color.FromArgb(226, 232, 240);

    public static Color Canvas { get; } = Color.FromArgb(248, 250, 252);

    public static Color Hover { get; } = Color.FromArgb(241, 245, 249);

    public static Color Good { get; } = Color.FromArgb(5, 150, 105);

    public static Color GoodSoft { get; } = Color.FromArgb(236, 253, 245);

    public static Color Warn { get; } = Color.FromArgb(180, 83, 9);

    public static Color Bad { get; } = Color.FromArgb(220, 38, 38);
}

/// <summary>How a step, or a section of the export, stands.</summary>
internal enum Mark
{
    /// <summary>Not reached yet.</summary>
    Waiting,

    /// <summary>The one being done now.</summary>
    Working,

    /// <summary>Done.</summary>
    Done,

    /// <summary>Stopped by a failure.</summary>
    Failed,
}

/// <summary>
/// A button of the window: the main one filled in the brand colour, the rest outlined. Which
/// is which can change as the export goes on - Connect leads until it has connected, then
/// Export does - so it is set rather than built in.
/// </summary>
internal sealed class ActionButton : Button
{
    private bool _primary;

    /// <summary>Makes the button.</summary>
    /// <param name="text">Its caption.</param>
    /// <param name="compact">As tall as the box it sits beside, rather than a full-size button.</param>
    public ActionButton(string text, bool compact = false)
    {
        Text = text;
        FlatStyle = FlatStyle.Flat;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = compact ? new Size(76, 25) : new Size(92, 34);
        Padding = compact ? new Padding(6, 0, 6, 0) : new Padding(10, 2, 10, 2);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        Restyle();
    }

    /// <summary>Makes it the main button, or a second one.</summary>
    /// <param name="primary">Whether it is the main one.</param>
    public void SetPrimary(bool primary)
    {
        if (_primary != primary)
        {
            _primary = primary;
            Restyle();
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Restyle();
    }

    private void Restyle()
    {
        if (_primary)
        {
            // A flat button draws itself the same enabled or not; a paler fill says it
            // cannot be pressed yet.
            BackColor = Enabled ? Palette.Brand : Palette.BrandMuted;
            ForeColor = Color.White;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Palette.BrandHover;
            FlatAppearance.MouseDownBackColor = Palette.BrandPressed;
        }
        else
        {
            BackColor = Color.White;
            ForeColor = Palette.Ink;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = Palette.Line;
            FlatAppearance.MouseOverBackColor = Palette.Hover;
            FlatAppearance.MouseDownBackColor = Palette.Locked;
        }
    }
}

/// <summary>
/// A small round mark beside a section of the export: an outline while it waits, filled
/// while it is read, a tick once it is, a cross where it failed.
/// </summary>
internal sealed class MarkDot : Control
{
    private Mark _mark;

    public MarkDot()
    {
        SetStyle(
            ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor,
            true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.Transparent;
        Size = new Size(14, 14);
        TabStop = false;
    }

    /// <summary>Shows how the section stands.</summary>
    /// <param name="mark">The mark.</param>
    public void SetMark(Mark mark)
    {
        if (_mark != mark)
        {
            _mark = mark;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Marks.Draw(e.Graphics, ClientRectangle, _mark, number: null, font: null);
    }
}

/// <summary>A step's number in a circle: grey until the step can be taken, the brand colour while it is the one to take, a tick once it is done.</summary>
internal sealed class StepBadge : Control
{
    private readonly int _number;
    private Mark _mark;

    public StepBadge(int number)
    {
        _number = number;
        SetStyle(
            ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor,
            true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.Transparent;
        Size = new Size(28, 28);
        TabStop = false;
    }

    /// <summary>Shows how the step stands.</summary>
    /// <param name="mark">The mark.</param>
    public void SetMark(Mark mark)
    {
        if (_mark != mark)
        {
            _mark = mark;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Marks.Draw(e.Graphics, ClientRectangle, _mark, _number, Font);
    }
}

/// <summary>Draws the round marks: a step's badge, and a section's dot.</summary>
internal static class Marks
{
    /// <summary>Draws a mark filling a square.</summary>
    /// <param name="graphics">Where to draw.</param>
    /// <param name="bounds">The square.</param>
    /// <param name="mark">How it stands.</param>
    /// <param name="number">A step's number, written in it while it is not done; null for a dot.</param>
    /// <param name="font">The number's font.</param>
    public static void Draw(Graphics graphics, Rectangle bounds, Mark mark, int? number, Font? font)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        int side = Math.Min(bounds.Width, bounds.Height) - 2;
        RectangleF circle = new(bounds.X + 1, bounds.Y + 1, side, side);
        float stroke = Math.Max(1.5F, side / 9F);

        Color fill = mark switch
        {
            Mark.Done => Palette.Good,
            Mark.Failed => Palette.Bad,
            Mark.Working => Palette.Brand,
            _ => number is null ? Color.Empty : Palette.Locked,
        };

        if (fill.IsEmpty)
        {
            using Pen outline = new(Palette.Subtle, stroke);
            graphics.DrawEllipse(outline, RectangleF.Inflate(circle, -stroke / 2, -stroke / 2));
            return;
        }

        using (SolidBrush brush = new(fill))
        {
            graphics.FillEllipse(brush, circle);
        }

        using Pen white = new(Color.White, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        if (mark == Mark.Done)
        {
            graphics.DrawLines(
                white,
                new PointF(circle.X + (side * 0.28F), circle.Y + (side * 0.52F)),
                new PointF(circle.X + (side * 0.44F), circle.Y + (side * 0.68F)),
                new PointF(circle.X + (side * 0.72F), circle.Y + (side * 0.36F)));
        }
        else if (mark == Mark.Failed)
        {
            float near = side * 0.32F;
            float far = side * 0.68F;
            graphics.DrawLine(white, circle.X + near, circle.Y + near, circle.X + far, circle.Y + far);
            graphics.DrawLine(white, circle.X + far, circle.Y + near, circle.X + near, circle.Y + far);
        }
        else if (number is not null && font is not null)
        {
            TextRenderer.DrawText(
                graphics,
                number.Value.ToString(System.Globalization.CultureInfo.CurrentCulture),
                font,
                Rectangle.Round(circle),
                mark == Mark.Working ? Color.White : Palette.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        else
        {
            // A section being read: a white centre, so it reads as "in hand" rather than done.
            float inset = side * 0.32F;
            using SolidBrush centre = new(Color.White);
            graphics.FillEllipse(centre, RectangleF.Inflate(circle, -inset, -inset));
        }
    }
}

/// <summary>One section of the export under the progress bar: its mark, its name, and its rows once read.</summary>
internal sealed class SectionLine : TableLayoutPanel
{
    private readonly MarkDot _dot = new() { Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) };

    private readonly Label _rows = new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.Right,
        ForeColor = Palette.Muted,
        Margin = Padding.Empty,
    };

    private Mark _mark;

    /// <summary>Lays out the line.</summary>
    /// <param name="label">What the section is called on screen.</param>
    public SectionLine(string label)
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Fill;
        ColumnCount = 3;
        RowCount = 1;
        Margin = new Padding(0, 2, 14, 2);
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Controls.Add(_dot, 0, 0);
        Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty }, 1, 0);
        Controls.Add(_rows, 2, 0);
    }

    /// <summary>Whether this is the section being read now.</summary>
    public bool Reading => _mark == Mark.Working;

    /// <summary>Shows how the section stands.</summary>
    /// <param name="mark">The mark.</param>
    /// <param name="rows">Its rows once read, or what is happening to it.</param>
    public void SetMark(Mark mark, string rows)
    {
        _mark = mark;
        _dot.SetMark(mark);
        _rows.Text = rows;
        _rows.ForeColor = mark == Mark.Failed ? Palette.Bad : Palette.Muted;
    }
}
