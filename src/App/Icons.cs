using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace NotasyonOtomasyonu.App;

/// <summary>Programın kendi çizgi simgeleri (emoji yerine). 24 birimlik ızgarada, yuvarlak uçlu tek kalınlıkta çizilir.</summary>
public enum Ico
{
    Settings, Search, Refresh, Reset, Undo, Redo, Edit, Exclude, Layers, CheckAll, CheckNone, Check, Close, Plus,
    Doc, Pdf, Files, DocAlert, Print, ClipboardList, ClipboardCheck, ListLines, Podium, Copy,
    TableCard, Badge, Pawn, Update, Download, Upload, Image, Table, Save, Folder, Clock, Eye, Trash, Template, Crop,
    Ok, Info, Warning, Error, Dot, ChevronLeft, ChevronRight, ArrowRight
}

public static class Icons
{
    /// <summary>Simgelerin varsayılan rengi (koyu yeşil, başlık ve çerçevelerle aynı).</summary>
    public static readonly Color Accent = Color.FromArgb(95, 122, 70);

    // Bitmap'ler iş parçacıkları arasında paylaşılamaz (GDI+ "Object is currently in use elsewhere"):
    // her arayüz iş parçacığının kendi önbelleği olur.
    [ThreadStatic] private static Dictionary<(Ico, int, int, int), Bitmap>? _cache;
    private static Dictionary<(Ico, int, int, int), Bitmap> Cache => _cache ??= new();
    private static readonly ConditionalWeakTable<ButtonBase, Spec> Specs = new();

    private sealed class Spec
    {
        public Ico Icon;
        public Color? Color;
        public float Scale;
        public Padding? BasePadding;   // düğmenin kendi iç boşluğu (simge için eklenen pay hariç)
    }

    /// <summary>
    /// Simgeyi verilen boyutta (px) ve renkte saydam bir resme çizer; aynı istekler önbellekten gelir.
    /// <paramref name="width"/>: resmin genişliği; simge <paramref name="offset"/> px içeriden başlar, gerisi saydam
    /// (düğmede yazıyla hizalamak için).
    /// </summary>
    public static Bitmap Render(Ico icon, int px, Color color, int width = 0, int offset = 0)
    {
        px = Math.Clamp(px, 8, 256);
        offset = Math.Clamp(offset, 0, 64);
        width = Math.Clamp(width, px + offset, 4096);
        var key = (icon, px, color.ToArgb(), width * 100 + offset);
        var cache = Cache;
        if (cache.TryGetValue(key, out var bmp)) return bmp;
        bmp = new Bitmap(width, px);
        using (var g = Graphics.FromImage(bmp)) Draw(g, icon, new RectangleF(offset, 0, px, px), color);
        cache[key] = bmp;
        return bmp;
    }

    /// <summary>
    /// Düğmeye yazısının önünde simge koyar. Boyut yazı tipinden gelir; yazı tipi, DPI ya da yazı rengi
    /// değişince simge yeniden çizilir. Renk verilmezse açık yazılı (yeşil) düğmede beyaz, ötekilerde koyu yeşil.
    /// </summary>
    public static T Set<T>(T b, Ico icon, Color? color = null, float scale = 1f) where T : ButtonBase
    {
        bool first = !Specs.TryGetValue(b, out var spec);
        spec ??= new Spec();
        spec.Icon = icon; spec.Color = color; spec.Scale = scale;
        if (first)
        {
            Specs.Add(b, spec);
            b.FontChanged += (_, _) => Apply(b);
            b.ForeColorChanged += (_, _) => Apply(b);
            b.TextChanged += (_, _) => Apply(b);
            b.DpiChangedAfterParent += (_, _) => Apply(b);
            b.HandleCreated += (_, _) => Apply(b);
            b.SizeChanged += (_, _) => Apply(b);
        }
        Apply(b);
        return b;
    }

    /// <summary>Düğmenin simgesini kaldırır.</summary>
    public static void Clear(ButtonBase b)
    {
        Specs.Remove(b);
        b.Image = null;
    }

    /// <summary>Menü öğesine simge (menünün simge boyutunda).</summary>
    public static T SetItem<T>(T item, Ico icon, Color? color = null) where T : ToolStripItem
    {
        int px = item.Owner?.ImageScalingSize.Height ?? 16;
        item.Image = Render(icon, px, color ?? Accent);
        return item;
    }

    internal static Ico? IconOf(ButtonBase b) => Specs.TryGetValue(b, out var s) ? s.Icon : null;

    internal static int SizeFor(Font font, float scale = 1f) => (int)Math.Round(font.Height * 0.9f * scale);

    [ThreadStatic] private static bool _applying;

    private static void Apply(ButtonBase b)
    {
        if (_applying || !Specs.TryGetValue(b, out var s)) return;
        _applying = true;
        try
        {
            var c = s.Color ?? (b.ForeColor.GetBrightness() > 0.8f ? b.ForeColor : Accent);
            int px = SizeFor(b.Font, s.Scale), gap = Math.Max(3, px / 3);
            var pad = s.BasePadding ??= b.Padding;
            bool text = !string.IsNullOrWhiteSpace(b.Text);
            if (!text)
            {
                b.Padding = pad;
                b.TextImageRelation = TextImageRelation.Overlay;
                b.ImageAlign = ContentAlignment.MiddleCenter;
                b.Image = Render(s.Icon, px, c);
                // yazısız düğme ekran okuyucuda ve otomasyonda adsız kalmasın (Axe.Windows: NameNotNull)
                if (string.IsNullOrEmpty(b.AccessibleName)) b.AccessibleName = SpokenName(s.Icon);
                return;
            }
            // Sabit genişlikte düğme: WinForms'un "simge yazının önünde" yerleşimi simge+yazı bloğunu ortalamaz
            // (büyük düğmede belirgin kayar), dar düğmede de simgeyi kaydırıp kesebilir. Yerleşim burada hesaplanır:
            // yazı simge payı kadar sağa itilir (iç boşluk); simge, yazının alanında ortalanan saydam bir resmin
            // içinde tam istenen yere (yazının soluna ya da düğmenin sol kenarına) çizilir.
            int w = TextRenderer.MeasureText(b.Text, b.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            bool center = IsCenter(b.TextAlign), left = IsLeft(b.TextAlign);
            int cw = b.ClientSize.Width, shift = px + gap, field = cw - pad.Horizontal - shift - 6;
            bool block = center && w + shift <= field;
            if (!block) shift = px + Math.Max(3, px / 4);   // simge sol kenarda: yazıya daha çok yer kalsın
            if (!b.AutoSize && (center || left))
            {
                b.Padding = new Padding(pad.Left + shift, pad.Top, pad.Right, pad.Bottom);
                b.TextImageRelation = TextImageRelation.Overlay;
                b.ImageAlign = ContentAlignment.MiddleCenter;
                b.Image = block
                    ? Render(s.Icon, px, c, w + 2 * shift)                                   // blok ortada
                    : Render(s.Icon, px, c, Math.Max(px, cw + shift - pad.Horizontal), 2);    // simge sol kenarda
            }
            else
            {
                // Kendi boyunu alan düğme (simge yazının hemen önünde, düğme bloğa göre büyür).
                b.Padding = pad;
                b.TextImageRelation = TextImageRelation.ImageBeforeText;
                b.ImageAlign = b.TextAlign;
                b.Image = Render(s.Icon, px, c, px + gap);
            }
        }
        finally { _applying = false; }
    }

    /// <summary>Yalnız simgeli düğmenin okunacak adı (pencere daha açıklayıcı bir ad vermediyse).</summary>
    internal static string SpokenName(Ico icon) => icon switch
    {
        Ico.Close => "Kaldır",
        Ico.ChevronLeft => "Önceki sayfa",
        Ico.ChevronRight => "Sonraki sayfa",
        Ico.Refresh => "Yenile",
        Ico.Reset => "Varsayılana dön",
        _ => icon.ToString()
    };

    private static bool IsCenter(ContentAlignment a)
        => a is ContentAlignment.MiddleCenter or ContentAlignment.TopCenter or ContentAlignment.BottomCenter;

    private static bool IsLeft(ContentAlignment a)
        => a is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft;

    /// <summary>Simgeli küçük açıklama yazısı (ör. tablo işaretlerinin anlamı).</summary>
    public static Label Caption(Ico icon, Color color, string text, Font? font = null)
    {
        var l = new CaptionLabel(icon, color) { AutoSize = true, Text = text, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 14, 0) };
        if (font is not null) l.Font = font;
        return l;
    }

    /// <summary>
    /// Simgeli açıklama: simge sol iç boşluğa çizilir, yazı ondan sonra başlar. (Label.Image iç boşluğun
    /// içine, yazının başladığı yere çizildiğinden simge ilk harfin üstüne biniyordu.)
    /// </summary>
    private sealed class CaptionLabel : Label
    {
        private readonly Ico _icon;
        private readonly Color _color;

        public CaptionLabel(Ico icon, Color color)
        {
            _icon = icon; _color = color;
            UpdatePadding();
        }

        private int IconPx => SizeFor(Font);

        private void UpdatePadding() => Padding = new Padding(IconPx + Math.Max(3, IconPx / 3), 0, 0, 0);

        protected override void OnFontChanged(EventArgs e) { UpdatePadding(); base.OnFontChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int px = IconPx;
            e.Graphics.DrawImage(Render(_icon, px, _color), 0, (ClientSize.Height - px) / 2, px, px);
        }
    }

    /// <summary>
    /// Tablodaki işaret hücrelerini (✓ ℹ ⚠ ✖ • ✎) simgeyle boyar; hücre değeri metin olarak kalır (kopyalama, sıralama).
    /// </summary>
    public static void PaintMarks(DataGridView grid, int column)
    {
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != column || e.Value is not string v || MarkIcon(v) is not { } m) return;
            e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
            int px = Math.Min(SizeFor(grid.Font), e.CellBounds.Height - 4);
            var r = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - px) / 2, e.CellBounds.Y + (e.CellBounds.Height - px) / 2, px, px);
            e.Graphics!.DrawImage(Render(m.Icon, px, m.Color), r);
            e.Handled = true;
        };
    }

    internal static (Ico Icon, Color Color)? MarkIcon(string mark) => mark.Trim() switch
    {
        "✓" => (Ico.Ok, Accent),
        "ℹ" => (Ico.Info, Color.SteelBlue),
        "⚠" => (Ico.Warning, Color.DarkGoldenrod),
        "✖" => (Ico.Error, Color.Firebrick),
        "•" => (Ico.Dot, Color.DarkGoldenrod),
        "✎" => (Ico.Edit, Color.Gray),
        _ => null
    };

    // ===================================================================== çizim

    /// <summary>Simgeyi dikdörtgenin içine çizer (24×24 ızgaradan ölçeklenir).</summary>
    public static void Draw(Graphics g, Ico icon, RectangleF r, Color color)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float size = Math.Min(r.Width, r.Height), k = size / 24f;
        g.TranslateTransform(r.X + (r.Width - size) / 2, r.Y + (r.Height - size) / 2);
        g.ScaleTransform(k, k);
        // Küçük boyutta çizgi en az ~1.3 px kalsın, büyükte 24'te 2 birim.
        float w = Math.Max(2f, 1.35f / k);
        using var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        new Canvas(g, pen, brush).Draw(icon);
        g.Restore(state);
    }

    private readonly record struct Canvas(Graphics G, Pen P, Brush B)
    {
        private static PointF[] Pts(float[] v)
        {
            var p = new PointF[v.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new PointF(v[2 * i], v[2 * i + 1]);
            return p;
        }

        private void L(float x1, float y1, float x2, float y2) => G.DrawLine(P, x1, y1, x2, y2);
        private void PL(params float[] v) => G.DrawLines(P, Pts(v));
        private void PG(params float[] v) => G.DrawPolygon(P, Pts(v));
        private void O(float cx, float cy, float r) => G.DrawEllipse(P, cx - r, cy - r, 2 * r, 2 * r);
        private void Dot(float cx, float cy, float r) => G.FillEllipse(B, cx - r, cy - r, 2 * r, 2 * r);
        private void A(float cx, float cy, float r, float start, float sweep) => G.DrawArc(P, cx - r, cy - r, 2 * r, 2 * r, start, sweep);

        private void R(float x, float y, float w, float h, float rad)
        {
            using var path = RoundRect(x, y, w, h, rad);
            G.DrawPath(P, path);
        }

        private static GraphicsPath RoundRect(float x, float y, float w, float h, float rad)
        {
            var p = new GraphicsPath();
            if (rad <= 0) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
            float d = 2 * rad;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Yay ve ucunda ok başı (sweep işareti yönü verir).</summary>
        private void ArcArrow(float cx, float cy, float r, float start, float sweep)
        {
            A(cx, cy, r, start, sweep);
            double t = (start + sweep) * Math.PI / 180;
            float tx = cx + r * (float)Math.Cos(t), ty = cy + r * (float)Math.Sin(t);
            float s = Math.Sign(sweep);
            float dx = -(float)Math.Sin(t) * s, dy = (float)Math.Cos(t) * s; // gidiş yönü
            Head(tx, ty, dx, dy, 4.2f);
        }

        private void Head(float tx, float ty, float dx, float dy, float len)
        {
            const double a = 0.75; // ~43°
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            float x1 = tx - len * (dx * c - dy * s), y1 = ty - len * (dx * s + dy * c);
            float x2 = tx - len * (dx * c + dy * s), y2 = ty - len * (-dx * s + dy * c);
            PL(x1, y1, tx, ty, x2, y2);
        }

        private void Paper() // kâğıt: kıvrık köşeli
        {
            PG(6, 2.5f, 14, 2.5f, 19, 7.5f, 19, 21.5f, 6, 21.5f);
            PL(14, 2.5f, 14, 7.5f, 19, 7.5f);
        }

        private void Clipboard()
        {
            PL(8.5f, 4.5f, 5, 4.5f, 5, 21.5f, 19, 21.5f, 19, 4.5f, 15.5f, 4.5f);
            R(8.5f, 2.5f, 7, 4, 1.2f);
        }

        public void Draw(Ico icon)
        {
            switch (icon)
            {
                case Ico.Settings:
                {
                    var pts = new List<float>();
                    for (int t = 0; t < 8; t++)
                    {
                        float a = t * 45f;
                        foreach (var (rad, da) in new[] { (7.4f, -17f), (9.8f, -9f), (9.8f, 9f), (7.4f, 17f) })
                        {
                            double ang = (a + da) * Math.PI / 180;
                            pts.Add(12 + rad * (float)Math.Cos(ang));
                            pts.Add(12 + rad * (float)Math.Sin(ang));
                        }
                    }
                    PG(pts.ToArray());
                    O(12, 12, 3);
                    break;
                }
                case Ico.Search:
                    O(10.5f, 10.5f, 6.5f);
                    L(15.5f, 15.5f, 20.5f, 20.5f);
                    break;
                case Ico.Refresh:
                    ArcArrow(12, 12, 8, 25, 290);
                    break;
                case Ico.Reset:
                    ArcArrow(12, 12, 8, 155, -290);
                    break;
                case Ico.Undo:
                    PL(9, 14, 4, 9, 9, 4);
                    L(4, 9, 14.5f, 9);
                    A(14.5f, 14.5f, 5.5f, 270, 180);
                    L(14.5f, 20, 10.5f, 20);
                    break;
                case Ico.Redo:
                    PL(15, 14, 20, 9, 15, 4);
                    L(20, 9, 9.5f, 9);
                    A(9.5f, 14.5f, 5.5f, 270, -180);
                    L(9.5f, 20, 13.5f, 20);
                    break;
                case Ico.Edit:
                    PG(4, 20, 5.3f, 15.6f, 16.6f, 4.3f, 19.7f, 7.4f, 8.4f, 18.7f);
                    L(14.1f, 6.8f, 17.2f, 9.9f);
                    break;
                case Ico.Exclude:
                    O(12, 12, 9);
                    L(5.7f, 5.7f, 18.3f, 18.3f);
                    break;
                case Ico.Layers:
                    PG(12, 2.5f, 21, 7.5f, 12, 12.5f, 3, 7.5f);
                    PL(3, 12, 12, 17, 21, 12);
                    PL(3, 16.5f, 12, 21.5f, 21, 16.5f);
                    break;
                case Ico.CheckAll:
                    R(3.5f, 3.5f, 17, 17, 3);
                    PL(8, 12, 11, 15, 16.5f, 9);
                    break;
                case Ico.CheckNone:
                    R(3.5f, 3.5f, 17, 17, 3);
                    break;
                case Ico.Check:
                    PL(4.5f, 12.5f, 9.5f, 17.5f, 19.5f, 7);
                    break;
                case Ico.Close:
                    L(6, 6, 18, 18);
                    L(18, 6, 6, 18);
                    break;
                case Ico.Plus:
                    L(12, 5, 12, 19);
                    L(5, 12, 19, 12);
                    break;
                case Ico.Doc:
                    Paper();
                    L(9, 12, 16, 12);
                    L(9, 15.5f, 16, 15.5f);
                    L(9, 19, 13, 19);
                    break;
                case Ico.Pdf:
                    Paper();
                    L(12.5f, 10.5f, 12.5f, 18);
                    PL(9.5f, 15, 12.5f, 18, 15.5f, 15);
                    break;
                case Ico.Files:
                    PG(8.5f, 5.5f, 15, 5.5f, 19.5f, 10, 19.5f, 21.5f, 8.5f, 21.5f);
                    PL(15, 5.5f, 15, 10, 19.5f, 10);
                    PL(5, 18.5f, 5, 2.5f, 12.5f, 2.5f);
                    break;
                case Ico.DocAlert:
                    Paper();
                    L(12.5f, 10.5f, 12.5f, 15);
                    Dot(12.5f, 18.3f, 1.2f);
                    break;
                case Ico.Print:
                    PL(6.5f, 9, 6.5f, 3, 17.5f, 3, 17.5f, 9);
                    PL(6.5f, 17.5f, 2.5f, 17.5f, 2.5f, 9, 21.5f, 9, 21.5f, 17.5f, 17.5f, 17.5f);
                    PG(6.5f, 14, 17.5f, 14, 17.5f, 21.5f, 6.5f, 21.5f);
                    Dot(18, 12, 1);
                    break;
                case Ico.ClipboardList:
                    Clipboard();
                    Dot(8.7f, 10.5f, 1); L(11.2f, 10.5f, 15.5f, 10.5f);
                    Dot(8.7f, 14, 1); L(11.2f, 14, 15.5f, 14);
                    Dot(8.7f, 17.5f, 1); L(11.2f, 17.5f, 15.5f, 17.5f);
                    break;
                case Ico.ClipboardCheck:
                    Clipboard();
                    PL(8.5f, 13.5f, 11, 16, 15.5f, 11);
                    break;
                case Ico.ListLines:
                    L(8.5f, 6, 21, 6); L(8.5f, 12, 21, 12); L(8.5f, 18, 21, 18);
                    Dot(4, 6, 1.3f); Dot(4, 12, 1.3f); Dot(4, 18, 1.3f);
                    break;
                case Ico.Podium:
                    PL(2.5f, 21.5f, 2.5f, 13, 8.8f, 13, 8.8f, 7.5f, 15.2f, 7.5f, 15.2f, 15.5f, 21.5f, 15.5f, 21.5f, 21.5f, 2.5f, 21.5f);
                    L(8.8f, 13, 8.8f, 21.5f);
                    L(15.2f, 15.5f, 15.2f, 21.5f);
                    Dot(12, 3.5f, 1.3f);
                    break;
                case Ico.Copy:
                    R(8.5f, 8.5f, 12, 12, 2);
                    PL(15.5f, 8.5f, 15.5f, 3.5f, 3.5f, 3.5f, 3.5f, 15.5f, 8.5f, 15.5f);
                    break;
                case Ico.TableCard: // masaya konan, iki yana açılan kart
                    PG(3.5f, 20, 7.8f, 4.5f, 16.2f, 4.5f, 20.5f, 20);
                    L(9.2f, 11, 14.8f, 11);
                    L(10, 14.5f, 14, 14.5f);
                    break;
                case Ico.Badge: // askılı yaka kartı
                    R(5, 3, 14, 18.5f, 2.5f);
                    L(10, 6.5f, 14, 6.5f);
                    O(12, 11.8f, 2.4f);
                    A(12, 19.5f, 4, 180, 180);
                    break;
                case Ico.Pawn: // satranç piyonu: sporcu
                    O(12, 6.2f, 3.2f);
                    L(8.8f, 11, 15.2f, 11);
                    PL(10.2f, 11, 8.6f, 17.5f);
                    PL(13.8f, 11, 15.4f, 17.5f);
                    R(5.5f, 17.5f, 13, 4, 1.2f);
                    break;
                case Ico.Update:
                    O(12, 12, 9.5f);
                    L(12, 16.5f, 12, 7.5f);
                    PL(8, 11.5f, 12, 7.5f, 16, 11.5f);
                    break;
                case Ico.Download:
                    L(12, 3, 12, 15);
                    PL(7, 10, 12, 15, 17, 10);
                    PL(3.5f, 15.5f, 3.5f, 20.5f, 20.5f, 20.5f, 20.5f, 15.5f);
                    break;
                case Ico.Upload:
                    L(12, 15, 12, 3);
                    PL(7, 8, 12, 3, 17, 8);
                    PL(3.5f, 15.5f, 3.5f, 20.5f, 20.5f, 20.5f, 20.5f, 15.5f);
                    break;
                case Ico.Image:
                    R(3, 4, 18, 16, 2.5f);
                    O(8.5f, 9.5f, 1.8f);
                    PL(21, 15.5f, 16, 10.5f, 6.5f, 20);
                    break;
                case Ico.Table:
                    R(3, 3.5f, 18, 17, 2.5f);
                    L(3, 9.2f, 21, 9.2f);
                    L(3, 14.8f, 21, 14.8f);
                    L(9.5f, 3.5f, 9.5f, 20.5f);
                    break;
                case Ico.Save:
                    PG(4, 3, 16.5f, 3, 20.5f, 7, 20.5f, 21, 4, 21);
                    PL(7.5f, 21, 7.5f, 14, 17, 14, 17, 21);
                    PL(7.5f, 3, 7.5f, 8, 14.5f, 8);
                    break;
                case Ico.Folder:
                    PG(3, 5, 9, 5, 11, 7.5f, 21, 7.5f, 21, 19.5f, 3, 19.5f);
                    break;
                case Ico.Clock:
                    O(12, 12, 9.5f);
                    PL(12, 6.5f, 12, 12, 15.5f, 14);
                    break;
                case Ico.Eye:
                {
                    using var p = new GraphicsPath();
                    p.AddBezier(2, 12, 5.5f, 5.5f, 18.5f, 5.5f, 22, 12);
                    p.AddBezier(22, 12, 18.5f, 18.5f, 5.5f, 18.5f, 2, 12);
                    G.DrawPath(P, p);
                    O(12, 12, 3);
                    break;
                }
                case Ico.Trash:
                    L(3.5f, 6.5f, 20.5f, 6.5f);
                    PL(9, 6.5f, 9, 3.5f, 15, 3.5f, 15, 6.5f);
                    PL(5.5f, 6.5f, 6.5f, 21, 17.5f, 21, 18.5f, 6.5f);
                    L(10, 10.5f, 10, 17);
                    L(14, 10.5f, 14, 17);
                    break;
                case Ico.Template:
                    R(3, 3, 18, 18, 2.5f);
                    L(3, 9, 21, 9);
                    L(9.5f, 9, 9.5f, 21);
                    break;
                case Ico.Crop:
                    PL(6, 2.5f, 6, 18, 21.5f, 18);
                    PL(2.5f, 6, 18, 6, 18, 21.5f);
                    break;
                case Ico.Ok:
                    O(12, 12, 9.5f);
                    PL(7.5f, 12.5f, 10.5f, 15.5f, 16.5f, 9.5f);
                    break;
                case Ico.Info:
                    O(12, 12, 9.5f);
                    L(12, 11, 12, 16.5f);
                    Dot(12, 7.6f, 1.2f);
                    break;
                case Ico.Warning:
                    PG(12, 3, 21.8f, 20.5f, 2.2f, 20.5f);
                    L(12, 9.5f, 12, 14);
                    Dot(12, 17.2f, 1.2f);
                    break;
                case Ico.Error:
                    O(12, 12, 9.5f);
                    L(8.8f, 8.8f, 15.2f, 15.2f);
                    L(15.2f, 8.8f, 8.8f, 15.2f);
                    break;
                case Ico.ChevronLeft:
                    PL(15, 5, 8, 12, 15, 19);
                    break;
                case Ico.ChevronRight:
                    PL(9, 5, 16, 12, 9, 19);
                    break;
                case Ico.ArrowRight:
                    L(4, 12, 20, 12);
                    PL(13.5f, 5.5f, 20, 12, 13.5f, 18.5f);
                    break;
                case Ico.Dot:
                    Dot(12, 12, 3.5f);
                    break;
            }
        }
    }
}
