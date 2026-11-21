using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Athletes;

/// <summary>Kartta kullanılan görseller (afiş, isimlik şeridi, iki logo). Çağıran yönetir.</summary>
public sealed record AthleteImages(Image? Banner, Image? PlateBanner, Image? Logo1, Image? Logo2);

/// <summary>
/// Sporcu yaka kartı ve isimliği. Düzenler turnuvalarda kullanılan Excel şablonlarından alındı:
/// <list type="bullet">
/// <item>Yaka kartı 95×135 mm, A4'e 2×2: üstte afiş (kenardan kenara, kırpılmadan; isterseniz altından kırpılır),
/// altında ad soyad (Arial Black), kategori ve okul/kulüp/il (Arial Narrow kalın), en altta iki yuvarlak logo ve
/// isteğe bağlı afişin alt bandından şerit; mavi çerçeve.</item>
/// <item>İsimlik 190×64 mm, A4'e 3 (ya da 190×68 mm, A4'e 4): üstte geniş afiş şeridi, altında solda kategori,
/// ince dikey çizgi, ad soyad ve kulüp/il, sağda UKD ve ELO kutuları (puanı olmayanda kutu basılmaz).</item>
/// </list>
/// Kartlar arasına ince kesim çizgisi, ızgara dışına köşe işaretleri konur.
/// </summary>
public sealed class AthleteSheetPrinter
{
    public const float Mm = 72f / 25.4f;
    private const float PageW = 595.28f, PageH = 841.89f;

    private readonly List<Athlete> _items;
    private readonly AthleteCardConfig _cfg;
    private readonly AthleteImages _img;
    private readonly string _eventName;
    private int _pageIndex;

    public bool IsPlate { get; }
    public float CardW { get; }
    public float CardH { get; }
    public int Cols { get; }
    public int Rows { get; }
    public int PerPage => Cols * Rows;
    public int CardCount => _items.Count;
    public int PageCount => (_items.Count + PerPage - 1) / PerPage;

    public AthleteSheetPrinter(IEnumerable<Athlete> items, AthleteCardConfig cfg, AthleteImages images, string eventName)
    {
        _items = items.ToList();
        _cfg = cfg;
        _img = images;
        _eventName = eventName;
        IsPlate = cfg.Kind == AthleteCardConfig.Plate;
        if (IsPlate)
        {
            bool four = cfg.PlatesPerPage >= 4;
            (CardW, CardH, Cols, Rows) = (190 * Mm, (four ? 68 : 64) * Mm, 1, four ? 4 : 3);
        }
        else (CardW, CardH, Cols, Rows) = (95 * Mm, 135 * Mm, 2, 2);
    }

    private PointF Origin => new((PageW - Cols * CardW) / 2, (PageH - Rows * CardH) / 2);

    // ================= Önizleme, yazıcı, PDF =================
    public bool PrintWithPreview(IWin32Window owner)
    {
        using var doc = BuildDocument();
        if (PrintRouter.IsSilent) return PrintRouter.Print(doc, owner, PreparePaper);
        var what = IsPlate ? "isimlik" : "yaka kartı";
        return PreviewDialog.Show(owner, doc, PageCount, $"{CardCount} {what} • {PageCount} sayfa A4 • {Cols}×{Rows} dizilim",
                                  f => PrintRouter.Print(doc, f, PreparePaper));
    }

    private static bool PreparePaper(PrintDocument doc, IWin32Window owner)
    {
        try
        {
            foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
                if (ps.Kind == PaperKind.A4) { doc.DefaultPageSettings.PaperSize = ps; break; }
        }
        catch (InvalidPrinterException) { }
        doc.DefaultPageSettings.Landscape = false;
        return true;
    }

    private PrintDocument BuildDocument()
    {
        var doc = new PrintDocument { DocumentName = $"{_eventName} - sporcu {(IsPlate ? "isimlikleri" : "yaka kartları")}" };
        PrintRouter.ApplyTarget(doc);
        PreparePaper(doc, null!);
        doc.OriginAtMargins = false;
        doc.BeginPrint += (_, _) => _pageIndex = 0;
        doc.PrintPage += (s, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Point;
            Prepare(g);
            bool preview = (s as PrintDocument)?.PrintController?.IsPreview == true;
            if (!preview) g.TranslateTransform(-e.PageSettings.HardMarginX / 100f * 72f, -e.PageSettings.HardMarginY / 100f * 72f);
            DrawPage(g, _pageIndex);
            _pageIndex++;
            e.HasMorePages = _pageIndex < PageCount;
        };
        return doc;
    }

    public void SavePdf(string path)
        => GdiPdf.Save(path, Enumerable.Range(0, PageCount).ToList(), PageW, PageH, (g, p) => { Prepare(g); DrawPage(g, p); }, dpi: 300);

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    public void DrawPage(Graphics g, int page)
    {
        var o = Origin;
        int start = page * PerPage, n = Math.Min(PerPage, _items.Count - start);
        for (int i = 0; i < n; i++)
        {
            int r = i / Cols, c = i % Cols;
            var rect = new RectangleF(o.X + c * CardW, o.Y + r * CardH, CardW, CardH);
            if (IsPlate) DrawPlate(g, rect, _items[start + i], _cfg, _img);
            else DrawBadge(g, rect, _items[start + i], _cfg, _img);
        }
        DrawCutLines(g, o, (n + Cols - 1) / Cols);
    }

    private void DrawCutLines(Graphics g, PointF o, int rows)
    {
        if (rows <= 0) return;
        float w = Cols * CardW, h = rows * CardH;
        using var cut = new Pen(Color.FromArgb(170, 170, 170), 0.3f) { DashStyle = DashStyle.Dash };
        for (int c = 0; c <= Cols; c++) g.DrawLine(cut, o.X + c * CardW, o.Y, o.X + c * CardW, o.Y + h);
        for (int r = 0; r <= rows; r++) g.DrawLine(cut, o.X, o.Y + r * CardH, o.X + w, o.Y + r * CardH);
        if (!_cfg.CropMarks) return;
        using var mark = new Pen(Color.Black, 0.5f);
        float gap = 2 * Mm, len = 5 * Mm;
        for (int c = 0; c <= Cols; c++)
        {
            float x = o.X + c * CardW;
            g.DrawLine(mark, x, o.Y - gap, x, Math.Max(1, o.Y - gap - len));
            g.DrawLine(mark, x, o.Y + h + gap, x, Math.Min(PageH - 1, o.Y + h + gap + len));
        }
        for (int r = 0; r <= rows; r++)
        {
            float y = o.Y + r * CardH;
            g.DrawLine(mark, o.X - gap, y, Math.Max(1, o.X - gap - len), y);
            g.DrawLine(mark, o.X + w + gap, y, Math.Min(PageW - 1, o.X + w + gap + len), y);
        }
    }

    // ================= Yaka kartı =================
    /// <summary>Afişin üst kısmı (altından kırpılmış) kaynak dikdörtgeni.</summary>
    private static RectangleF PosterSource(Image img, int cropBottomPercent)
        => new(0, 0, img.Width, img.Height * (1 - Math.Clamp(cropBottomPercent, 0, 45) / 100f));

    public static void DrawBadge(Graphics g, RectangleF r, Athlete a, AthleteCardConfig cfg, AthleteImages img)
    {
        float mm = r.Width / 95f;               // 95 mm genişliğe göre ölçek (önizlemede de aynı oran)
        float vy = r.Height / 135f;
        var frame = CardLayout.ParseColor(cfg.FrameColor, Color.FromArgb(0, 112, 192));
        var text = CardLayout.ParseColor(cfg.TextColor, Color.FromArgb(31, 56, 100));
        using (var white = new SolidBrush(Color.White)) g.FillRectangle(white, r);
        float inset = 0.6f * mm;
        var inner = RectangleF.Inflate(r, -inset, -inset);

        // Afiş: üstte, iç çerçevenin kenarından kenarına; kırpılmaz (oran korunur), en çok kartın %42'si
        float top = inner.Y;
        if (img.Banner is { } banner)
        {
            var src = PosterSource(banner, cfg.CropBottomPercent);
            float aspect = src.Width / Math.Max(1, src.Height);
            float w = inner.Width, h = w / aspect, cap = r.Height * 0.42f;
            if (h > cap) { h = cap; w = h * aspect; }
            var dst = new RectangleF(inner.X + (inner.Width - w) / 2, inner.Y, w, h);
            g.DrawImage(banner, dst, src, GraphicsUnit.Pixel);
            top = dst.Bottom;
        }
        else
        {
            var box = new RectangleF(inner.X + 4 * mm, inner.Y + 4 * mm, inner.Width - 8 * mm, (inner.Width - 8 * mm) * 9 / 16);
            using var dash = new Pen(Color.FromArgb(160, 160, 160), 0.8f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(dash, box.X, box.Y, box.Width, box.Height);
            AthleteText.Draw(g, "TURNUVA AFİŞİ", box, AthleteText.Narrow, 12, 8, 1, Color.FromArgb(150, 150, 150), StringAlignment.Center);
            top = box.Bottom + 4 * mm;
        }
        using (var line = new Pen(frame, 0.7f * mm)) g.DrawLine(line, inner.X, top + 0.35f * mm, inner.Right, top + 0.35f * mm);

        // Alt şerit (afişin en alt bandı) ve iki yuvarlak logo
        float bottom = inner.Bottom;
        if (cfg.FooterBand)
        {
            var band = new RectangleF(inner.X, inner.Bottom - 7.6f * mm * vy, inner.Width, 7.6f * mm * vy);
            if (img.Banner is { } b2)
                g.DrawImage(b2, band, new RectangleF(0, b2.Height * 0.888f, b2.Width, b2.Height * 0.112f), GraphicsUnit.Pixel);
            else using (var fb = new SolidBrush(frame)) g.FillRectangle(fb, band);
            bottom = band.Top;
        }
        var logos = new[] { img.Logo1, img.Logo2 }.Where(l => l is not null).ToList();
        float logoD = 20 * mm;
        if (logos.Count > 0)
        {
            float cy = inner.Bottom - (cfg.FooterBand ? 11.5f : 12.5f) * mm;
            float[] xs = logos.Count == 1 ? new[] { r.X + r.Width / 2 } : new[] { r.X + r.Width * 0.244f, r.X + r.Width * 0.765f };
            for (int i = 0; i < logos.Count; i++)
            {
                var slot = new RectangleF(xs[i] - logoD / 2, cy - logoD / 2, logoD, logoD);
                DrawRoundLogo(g, logos[i]!, slot, text, mm);
            }
            bottom = Math.Min(bottom, cy - logoD / 2 - 1 * mm);
        }

        // Yazılar: ad soyad, kategori, okul / kulüp / il
        float y0 = top + 3 * mm, area = Math.Max(10 * mm, bottom - y0);
        var nameBox = new RectangleF(inner.X + 3 * mm, y0, inner.Width - 6 * mm, area * 0.42f);
        var catBox = new RectangleF(inner.X + 3 * mm, y0 + area * 0.45f, inner.Width - 6 * mm, area * 0.22f);
        var l3Box = new RectangleF(inner.X + 3 * mm, y0 + area * 0.68f, inner.Width - 6 * mm, area * 0.30f);
        AthleteText.Draw(g, a.Name, nameBox, AthleteText.Black, 20, 9, 2, text, StringAlignment.Center);
        if (!string.IsNullOrWhiteSpace(a.Category))
            AthleteText.Draw(g, Upper(a.Category), catBox, AthleteText.Narrow, 14, 7, 2, text, StringAlignment.Center);
        if (!string.IsNullOrWhiteSpace(a.Line3))
            AthleteText.Draw(g, Upper(a.Line3), l3Box, AthleteText.Narrow, 16, 7, 2, text, StringAlignment.Center);

        using var outer = new Pen(frame, 0.7f * mm);
        g.DrawRectangle(outer, r.X + 0.35f * mm, r.Y + 0.35f * mm, r.Width - 0.7f * mm, r.Height - 0.7f * mm);
    }

    /// <summary>
    /// Yuvarlak logo: beyaz daire, logo daireye sığdırılır (kare/yuvarlak logolarda daireyi doldurur, köşeler
    /// kırpılır), ince koyu halka. Şeffaf PNG de şeridin üstünde okunur.
    /// </summary>
    private static void DrawRoundLogo(Graphics g, Image logo, RectangleF slot, Color ring, float mm)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(slot);
        using (var white = new SolidBrush(Color.White)) g.FillPath(white, path);
        var state = g.Save();
        g.SetClip(path);
        float ratio = logo.Width / (float)Math.Max(1, logo.Height);
        // neredeyse kare logolar daireyi kaplar (kenardaki beyaz pay kırpılır); belirgin dikdörtgenler içine sığar
        var inner = RectangleF.Inflate(slot, -0.6f * mm, -0.6f * mm);
        RectangleF dst;
        if (ratio is > 0.8f and < 1.25f)
        {
            float s = Math.Max(inner.Width / logo.Width, inner.Height / logo.Height);
            dst = new RectangleF(inner.X + (inner.Width - logo.Width * s) / 2, inner.Y + (inner.Height - logo.Height * s) / 2, logo.Width * s, logo.Height * s);
        }
        else dst = CardLayout.FitImage(new SizeF(logo.Width, logo.Height), RectangleF.Inflate(inner, -1.5f * mm, -1.5f * mm));
        g.DrawImage(logo, dst);
        g.Restore(state);
        using var pen = new Pen(ring, 0.8f * mm);
        g.DrawEllipse(pen, slot.X + 0.4f * mm, slot.Y + 0.4f * mm, slot.Width - 0.8f * mm, slot.Height - 0.8f * mm);
    }

    // ================= İsimlik =================
    public static void DrawPlate(Graphics g, RectangleF r, Athlete a, AthleteCardConfig cfg, AthleteImages img)
    {
        float mm = r.Width / 190f;
        var frame = CardLayout.ParseColor(cfg.FrameColor, Color.FromArgb(0, 32, 96));
        var text = CardLayout.ParseColor(cfg.TextColor, Color.FromArgb(31, 56, 100));
        using (var white = new SolidBrush(Color.White)) g.FillRectangle(white, r);

        // Üst şerit: isimlik görseli (yoksa afiş). Oranı şeride yakınsa şeridi kaplar, değilse sığdırılır.
        float stripH = r.Height * 0.56f;
        var strip = new RectangleF(r.X, r.Y, r.Width, stripH);
        var banner = img.PlateBanner ?? img.Banner;
        if (banner is not null)
        {
            var src = img.PlateBanner is null ? PosterSource(banner, cfg.CropBottomPercent) : new RectangleF(0, 0, banner.Width, banner.Height);
            float aspect = src.Width / Math.Max(1, src.Height), slotAspect = strip.Width / strip.Height;
            var state = g.Save();
            g.SetClip(strip);
            if (aspect / slotAspect is > 0.75f and < 1.35f)
            {
                // kapla: taşan kısım ortadan kırpılır
                float scale = Math.Max(strip.Width / src.Width, strip.Height / src.Height);
                float w = src.Width * scale, h = src.Height * scale;
                g.DrawImage(banner, new RectangleF(strip.X + (strip.Width - w) / 2, strip.Y + (strip.Height - h) / 2, w, h), src, GraphicsUnit.Pixel);
            }
            else
            {
                float scale = Math.Min(strip.Width / src.Width, strip.Height / src.Height);
                float w = src.Width * scale, h = src.Height * scale;
                g.DrawImage(banner, new RectangleF(strip.X + (strip.Width - w) / 2, strip.Y + (strip.Height - h) / 2, w, h), src, GraphicsUnit.Pixel);
            }
            g.Restore(state);
        }
        else
        {
            using var dash = new Pen(Color.FromArgb(170, 170, 170), 0.8f) { DashStyle = DashStyle.Dash };
            var box = RectangleF.Inflate(strip, -3 * mm, -3 * mm);
            g.DrawRectangle(dash, box.X, box.Y, box.Width, box.Height);
            AthleteText.Draw(g, "TURNUVA AFİŞİ / ŞERİT", box, AthleteText.Narrow, 14, 8, 1, Color.FromArgb(150, 150, 150), StringAlignment.Center);
        }
        using (var line = new Pen(frame, 0.4f * mm)) g.DrawLine(line, r.X, strip.Bottom, r.Right, strip.Bottom);

        // Alt kısım: kategori | ad soyad + kulüp/il | UKD ELO
        float lowTop = strip.Bottom, lowH = r.Bottom - lowTop;
        var catBox = new RectangleF(r.X + 1.5f * mm, lowTop + 1.5f * mm, 30 * mm, lowH - 3 * mm);
        if (!string.IsNullOrWhiteSpace(a.Category))
            AthleteText.Draw(g, Upper(a.Category), catBox, AthleteText.NarrowRegular, 16, 7, 3, text, StringAlignment.Center);
        using (var v = new Pen(Color.FromArgb(40, 40, 40), 0.3f * mm))
            g.DrawLine(v, r.X + 33.6f * mm, lowTop + lowH * 0.24f, r.X + 33.6f * mm, lowTop + lowH * 0.76f);

        // Puan kutuları (sağda, alta yaslı): ELO en sağda, UKD solunda; puanı olmayan basılmaz
        float boxW = 16.3f * mm, boxH = 8.9f * mm, right = r.Right - 3.5f * mm;
        float boxTop = r.Bottom - 1.5f * mm - boxH;
        float textRight = r.Right - 4 * mm;
        void Score(string label, int value)
        {
            var box = new RectangleF(right - boxW, boxTop, boxW, boxH);
            using (var pen = new Pen(text, 0.26f * mm)) g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            AthleteText.Draw(g, value.ToString(), RectangleF.Inflate(box, -0.8f * mm, -0.5f * mm), AthleteText.Narrow, 16, 7, 1, Color.Black, StringAlignment.Center);
            AthleteText.Draw(g, label, new RectangleF(box.X, box.Y - 5.6f * mm, box.Width, 5.2f * mm), AthleteText.Narrow, 10, 6, 1, text, StringAlignment.Center);
            textRight = box.X - 3 * mm;
            right = box.X - 1 * mm;
        }
        if (cfg.ShowElo && a.Elo is > 0) Score("ELO", a.Elo.Value);
        if (cfg.ShowUkd && a.Ukd is > 0) Score("UKD", a.Ukd.Value);

        float tx = r.X + 35.5f * mm;
        var nameBox = new RectangleF(tx, lowTop + 1.5f * mm, textRight - tx, lowH * 0.48f);
        var l3Box = new RectangleF(tx, lowTop + lowH * 0.52f, textRight - tx, lowH * 0.44f);
        AthleteText.Draw(g, a.Name, nameBox, AthleteText.Black, 14, 7, 1, Color.Black, StringAlignment.Near);
        if (!string.IsNullOrWhiteSpace(a.Line3))
            AthleteText.Draw(g, Upper(a.Line3), l3Box, AthleteText.Narrow, 16, 7, 2, Color.Black, StringAlignment.Near);

        using var outer = new Pen(frame, 0.35f * mm);
        g.DrawRectangle(outer, r.X + 0.2f * mm, r.Y + 0.2f * mm, r.Width - 0.4f * mm, r.Height - 0.4f * mm);
    }

    private static string Upper(string s) => s.Trim().ToUpper(new System.Globalization.CultureInfo("tr-TR"));
}

/// <summary>
/// Kart yazıları: Excel şablonlarındaki yazı tipleri (Arial Black, Arial Narrow kalın). Bilgisayarda yoksa
/// Windows'la gelen benzerleri (Segoe UI Black, Bahnschrift Condensed, Arial) kullanılır.
/// </summary>
public static class AthleteText
{
    public sealed record Face(string[] Families, FontStyle Style);

    public static readonly Face Black = new(new[] { "Arial Black", "Segoe UI Black", "Arial" }, FontStyle.Regular);
    public static readonly Face Narrow = new(new[] { "Arial Narrow", "Bahnschrift Condensed", "Arial" }, FontStyle.Bold);
    public static readonly Face NarrowRegular = new(new[] { "Arial Narrow", "Bahnschrift Condensed", "Arial" }, FontStyle.Regular);

    private static readonly Dictionary<string, FontFamily?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Bitmap MeasureBmp = new(4, 4);

    private static (FontFamily Family, FontStyle Style) Resolve(Face face)
    {
        lock (Cache)
        {
            foreach (var name in face.Families)
            {
                if (!Cache.TryGetValue(name, out var fam))
                {
                    try { fam = new FontFamily(name); if (!string.Equals(fam.Name, name, StringComparison.OrdinalIgnoreCase)) fam = null; }
                    catch { fam = null; }
                    Cache[name] = fam;
                }
                if (fam is null) continue;
                var style = fam.IsStyleAvailable(face.Style) ? face.Style : fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
                bool arialFallback = name == "Arial" && face == Black;
                return (fam, arialFallback ? FontStyle.Bold : style);
            }
            return (FontFamily.GenericSansSerif, FontStyle.Bold);
        }
    }

    /// <summary>Kullanılan yazı tipinin adı (bilgi için).</summary>
    public static string NameOf(Face face) => Resolve(face).Family.Name;

    public static Font Create(Face face, float pt)
    {
        var (fam, style) = Resolve(face);
        return new Font(fam, pt, style, GraphicsUnit.Point);
    }

    private const float RefPt = 100f;
    private static readonly Dictionary<(Face, string), float> Widths = new();

    /// <summary>
    /// Metin genişliği (punto). Genişlik puntoyla doğrusal olduğundan her metin bir kez 100 puntoda ölçülüp
    /// ölçeklenir: sığdırma her kartta onlarca ölçüm yapar, her seferinde yazı tipi oluşturmak yavaştı.
    /// </summary>
    private static float Width(Face face, string s, float pt)
    {
        lock (MeasureBmp)
        {
            if (!Widths.TryGetValue((face, s), out var w))
            {
                using var g = Graphics.FromImage(MeasureBmp);
                g.PageUnit = GraphicsUnit.Point;
                using var f = Create(face, RefPt);
                w = g.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic).Width;
                if (Widths.Count > 20000) Widths.Clear();
                Widths[(face, s)] = w;
            }
            return w * pt / RefPt;
        }
    }

    private const float LineSpacing = 1.15f;

    /// <summary>Metni kutuya en büyük puntoda (en çok <paramref name="maxLines"/> satır) yerleştirir.</summary>
    public static (List<string> Lines, float Pt) Fit(string textValue, RectangleF box, Face face, float maxPt, float minPt, int maxLines)
    {
        var words = (textValue ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return (new List<string>(), maxPt);
        // önce en çok maxLines satırda, minPt'ye kadar; sığmazsa (çok uzun ad) daha küçük puntoya inilir, taşmaz
        for (float pt = maxPt; pt >= 4; pt -= Math.Max(0.5f, pt * 0.04f))
        {
            var wrapped = Wrap(words, box.Width * 0.97f, pt, face);
            if (wrapped is not null && wrapped.Count <= maxLines && wrapped.Count * pt * LineSpacing <= box.Height * 0.98f) return (wrapped, pt);
        }
        return (new List<string> { string.Join(' ', words) }, 4);
    }

    private static List<string>? Wrap(string[] words, float width, float pt, Face face)
    {
        var lines = new List<string>();
        var cur = "";
        foreach (var w in words)
        {
            if (Width(face, w, pt) > width) return null;
            var cand = cur.Length == 0 ? w : cur + " " + w;
            if (Width(face, cand, pt) <= width) cur = cand;
            else { lines.Add(cur); cur = w; }
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }

    public static void Draw(Graphics g, string textValue, RectangleF box, Face face, float maxPt, float minPt, int maxLines, Color color, StringAlignment align)
    {
        var (lines, pt) = Fit(textValue, box, face, maxPt, minPt, maxLines);
        if (lines.Count == 0) return;
        using var f = Create(face, pt);
        using var brush = new SolidBrush(color);
        using var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = align, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        float lh = pt * LineSpacing;
        float y = box.Y + (box.Height - lh * lines.Count) / 2;
        foreach (var line in lines)
        {
            var lineBox = align == StringAlignment.Center ? new RectangleF(box.X - 50, y, box.Width + 100, lh) : new RectangleF(box.X, y, box.Width + 50, lh);
            g.DrawString(line, f, brush, lineBox, fmt);
            y += lh;
        }
    }
}
