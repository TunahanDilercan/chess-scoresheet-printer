using System.Drawing.Drawing2D;

namespace NotasyonOtomasyonu.App;

/// <summary>Açık/kapalı anahtar görünümlü onay kutusu (yeşil = açık).</summary>
public sealed class ToggleSwitch : CheckBox
{
    private static readonly Color On = Color.FromArgb(118, 150, 86);
    private static readonly Color Off = Color.FromArgb(190, 190, 184);

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
        AutoSize = false;
        Height = 26;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? BackColor);
        int h = Height - 6, w = (int)(h * 1.9f), top = 3;
        var track = new Rectangle(1, top, w, h);
        using (var path = Pill(track))
        using (var b = new SolidBrush(Enabled ? (Checked ? On : Off) : Color.Gainsboro))
            g.FillPath(b, path);
        int knob = h - 4;
        int kx = Checked ? track.Right - knob - 2 : track.Left + 2;
        using (var kb = new SolidBrush(Color.White)) g.FillEllipse(kb, kx, top + 2, knob, knob);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(w + 10, 0, Width - w - 10, Height), Enabled ? ForeColor : SystemColors.GrayText,
                              TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(w + 8, 2, Width - w - 10, Height - 4));
    }

    private static GraphicsPath Pill(Rectangle r)
    {
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, r.Height, r.Height, 90, 180);
        p.AddArc(r.Right - r.Height, r.Y, r.Height, r.Height, 270, 180);
        p.CloseFigure();
        return p;
    }
}
