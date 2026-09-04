using System.Drawing;
using System.Drawing.Drawing2D;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.TemplateDesigner;

/// <summary>
/// Tasarımcı tuvalinde bir overlay alanını temsil eden, fare ile taşınıp boyutlandırılabilen kutu.
/// Sağ-alt köşedeki tutamaçtan boyutlandırılır; gövdesinden sürüklenir.
/// </summary>
public sealed class FieldBoxControl : Control
{
    private const int Grip = 12;
    private static readonly Color Accent = Color.FromArgb(118, 150, 86);

    private bool _dragging, _resizing;
    private Point _start;
    private Rectangle _startBounds;
    private bool _selected;

    public OverlayField Field { get; }
    public string PreviewText { get; set; } = "";
    public string Caption { get; set; } = "";

    public event EventHandler? GeometryChanged;
    public event EventHandler? SelectedNow;

    public FieldBoxControl(OverlayField field)
    {
        Field = field;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.FromArgb(255, 255, 255);
        Cursor = Cursors.SizeAll;
        MinimumSize = new Size(24, 14);
    }

    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Invalidate(); } }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        BringToFront();
        Selected = true;
        SelectedNow?.Invoke(this, EventArgs.Empty);

        _start = e.Location;
        _startBounds = Bounds;
        if (e.X >= Width - Grip && e.Y >= Height - Grip) _resizing = true;
        else _dragging = true;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            int nx = Left + (e.X - _start.X);
            int ny = Top + (e.Y - _start.Y);
            var p = Parent;
            if (p is not null)
            {
                nx = Math.Max(0, Math.Min(nx, p.ClientSize.Width - Width));
                ny = Math.Max(0, Math.Min(ny, p.ClientSize.Height - Height));
            }
            Location = new Point(nx, ny);
        }
        else if (_resizing)
        {
            int nw = Math.Max(MinimumSize.Width, e.X);
            int nh = Math.Max(MinimumSize.Height, e.Y);
            var p = Parent;
            if (p is not null)
            {
                nw = Math.Min(nw, p.ClientSize.Width - Left);
                nh = Math.Min(nh, p.ClientSize.Height - Top);
            }
            Size = new Size(nw, nh);
        }
        else
        {
            Cursor = (e.X >= Width - Grip && e.Y >= Height - Grip) ? Cursors.SizeNWSE : Cursors.SizeAll;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging || _resizing) GeometryChanged?.Invoke(this, EventArgs.Empty);
        _dragging = _resizing = false;
        Capture = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);

        using (var fill = new SolidBrush(Color.FromArgb(_selected ? 40 : 22, Accent)))
            g.FillRectangle(fill, r);

        using (var pen = new Pen(_selected ? Accent : Color.FromArgb(120, 120, 120), _selected ? 2f : 1f))
        {
            pen.DashStyle = _selected ? DashStyle.Solid : DashStyle.Dash;
            g.DrawRectangle(pen, r);
        }

        // Etiket + örnek metin
        var label = Caption;
        var text = string.IsNullOrEmpty(PreviewText) ? "(boş)" : PreviewText;
        using var capFont = new Font("Segoe UI", 6.5f, FontStyle.Bold);
        using var txtFont = new Font("Segoe UI", 8f);
        using var capBr = new SolidBrush(Accent);
        g.DrawString(label, capFont, capBr, 2, 1);
        TextRenderer.DrawText(g, text, txtFont, new Rectangle(2, 13, Width - 4, Height - 14),
            Color.Black, TextFormatFlags.EndEllipsis | TextFormatFlags.Left);

        // Boyutlandırma tutamacı
        using var gb = new SolidBrush(Accent);
        g.FillRectangle(gb, Width - Grip, Height - Grip, Grip - 2, Grip - 2);
    }
}
