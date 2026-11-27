using System.Drawing.Drawing2D;

namespace NotasyonOtomasyonu.App.Cards;

/// <summary>Masa kartı türü: kategori kartı, yönlendirme levhası (yazı + ok) ya da yasak levhası.</summary>
public enum CardKind { Category, Sign, Ban }

/// <summary>Yönlendirme levhasının altındaki ok.</summary>
public enum CardArrow { None, Left, Right, Up, Down }

/// <summary>
/// Salon levhaları: üst kısım kategori kartlarıyla aynıdır (afiş / logolar); alt bantta siyah büyük yazı ve
/// isteğe bağlı yön oku ya da yasak simgeleri (sigara, cep telefonu, elektronik cihaz, yiyecek-içecek).
/// Simgeler vektör çizilir: her boyutta ve yazıcıda keskin.
/// </summary>
public static class CardSigns
{
    public static readonly Color Ink = Color.FromArgb(20, 20, 20);
    public static readonly Color BanRed = Color.FromArgb(211, 32, 39);

    /// <summary>Varsayılan levhalar (sırasıyla pencerede listelenir).</summary>
    public static readonly string[] DefaultSigns = { "İNCELEME MASASI", "KAYIT KONTROL", "WC", "SPORCU GİRİŞİ", "SPORCU ÇIKIŞI" };

    public enum BanIcon { Smoking, Phone, Electronics, FoodDrink }

    public static readonly (BanIcon Icon, string Label)[] Bans =
    {
        (BanIcon.Smoking, "SİGARA"), (BanIcon.Phone, "CEP TELEFONU"), (BanIcon.Electronics, "ELEKTRONİK CİHAZ"), (BanIcon.FoodDrink, "YİYECEK - İÇECEK"),
    };

    /// <summary>Levhada yazının ve okun alanları: ok varsa yazı üstte, ok altta.</summary>
    public static (RectangleF Text, RectangleF Arrow) SplitSign(RectangleF box, CardArrow arrow)
    {
        if (arrow == CardArrow.None) return (box, RectangleF.Empty);
        float textH = box.Height * 0.58f;
        return (new RectangleF(box.X, box.Y, box.Width, textH), new RectangleF(box.X, box.Y + textH, box.Width, box.Height - textH));
    }

    /// <summary>Kalın, dolu ok (levha yazısının altında, ortalı).</summary>
    public static void DrawArrow(Graphics g, RectangleF area, CardArrow dir, Color color)
    {
        if (dir == CardArrow.None || area.Width <= 0 || area.Height <= 0) return;
        bool horizontal = dir is CardArrow.Left or CardArrow.Right;
        // ok gövdesi uzunluğu L, kalınlığı T, baş genişliği H (yatay okta alanın yüksekliğine göre)
        float size = Math.Min(area.Height * 0.86f, horizontal ? area.Width * 0.22f : area.Width * 0.3f);
        float len = horizontal ? size * 2.6f : size * 0.95f, thick = size * 0.30f, head = size * 0.80f, headLen = size * 0.62f;
        if (!horizontal) { len = area.Height * 0.86f; head = Math.Min(head, len * 0.95f); headLen = Math.Min(headLen, len * 0.55f); }
        var c = new PointF(area.X + area.Width / 2, area.Y + area.Height / 2);
        // sağa bakan ok, merkezde; sonra yöne döndürülür
        var pts = new[]
        {
            new PointF(-len / 2, -thick / 2), new PointF(len / 2 - headLen, -thick / 2), new PointF(len / 2 - headLen, -head / 2),
            new PointF(len / 2, 0), new PointF(len / 2 - headLen, head / 2), new PointF(len / 2 - headLen, thick / 2), new PointF(-len / 2, thick / 2),
        };
        float angle = dir switch { CardArrow.Left => 180, CardArrow.Up => -90, CardArrow.Down => 90, _ => 0 };
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(c.X, c.Y);
        g.RotateTransform(angle);
        using (var b = new SolidBrush(color)) g.FillPolygon(b, pts);
        g.Restore(state);
    }

    /// <summary>Yasak levhasının bandı: simgeler yan yana, altlarında adları.</summary>
    public static void DrawBanBand(Graphics g, RectangleF box, string? font)
    {
        int n = Bans.Length;
        float gap = box.Width * 0.03f;
        float colW = (box.Width - gap * (n - 1)) / n;
        float labelH = Math.Max(18, box.Height * 0.2f);
        float icon = Math.Min(colW * 0.82f, box.Height - labelH - 8);
        if (icon <= 10) return;
        float top = box.Y + (box.Height - icon - labelH - 6) / 2;
        var labels = Bans.Select(b => b.Label).ToList();
        var labelBox = new RectangleF(0, 0, colW, labelH);
        float pt = CardLayout.CommonPt(labels, labelBox, 40, font, 8, 2);
        for (int i = 0; i < n; i++)
        {
            float x = box.X + i * (colW + gap);
            DrawBan(g, new RectangleF(x + (colW - icon) / 2, top, icon, icon), Bans[i].Icon);
            var lb = new RectangleF(x, top + icon + 6, colW, labelH);
            CardLayout.DrawLines(g, CardLayout.FitAt(labels[i], lb, pt, font, 2), lb, Ink, font);
        }
    }

    /// <summary>Tek yasak simgesi: kırmızı daire, siyah resim, üstte kırmızı eğik çizgi.</summary>
    public static void DrawBan(Graphics g, RectangleF r, BanIcon icon)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 100f, r.Height / 100f);   // simge 100×100 birimlik kutuda çizilir
        using (var white = new SolidBrush(Color.White)) g.FillEllipse(white, 6, 6, 88, 88);
        using (var ink = new SolidBrush(Ink))
        using (var pen = new Pen(Ink, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            switch (icon)
            {
                case BanIcon.Smoking: Cigarette(g, ink, pen); break;
                case BanIcon.Phone: Phone(g, ink, pen); break;
                case BanIcon.Electronics: Headphones(g, ink, pen); break;
                case BanIcon.FoodDrink: FoodDrink(g, ink, pen); break;
            }
        using (var red = new Pen(BanRed, 9f))
        {
            g.DrawEllipse(red, 6, 6, 88, 88);
            red.StartCap = red.EndCap = LineCap.Flat;
            // ISO 7010 yasak işareti: sol üstten sağ alta 45°
            float k = 44 * 0.7071f;
            g.DrawLine(red, 50 - k, 50 - k, 50 + k, 50 + k);
        }
        g.Restore(state);
    }

    private static void Cigarette(Graphics g, Brush ink, Pen pen)
    {
        g.FillRectangle(ink, 18, 56, 18, 11);                 // filtre
        using (var p = new Pen(Ink, 3f)) g.DrawRectangle(p, 36, 56, 40, 11);   // gövde
        g.FillRectangle(ink, 77, 56, 5, 11);                  // yanan uç
        using var smoke = new Pen(Ink, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawBezier(smoke, 72, 50, 66, 42, 78, 36, 72, 26);
        g.DrawBezier(smoke, 81, 50, 75, 42, 87, 36, 81, 26);
    }

    private static void Phone(Graphics g, Brush ink, Pen pen)
    {
        using var body = RoundRect(new RectangleF(34, 18, 32, 64), 6);
        g.FillPath(ink, body);
        using (var white = new SolidBrush(Color.White)) g.FillRectangle(white, 38, 26, 24, 42);   // ekran
        using (var white = new SolidBrush(Color.White)) g.FillEllipse(white, 46.5f, 72, 7, 7);     // tuş
    }

    private static void Headphones(Graphics g, Brush ink, Pen pen)
    {
        using (var band = new Pen(Ink, 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(band, 24, 22, 52, 56, 180, 180);       // kafa bandı
        using (var cup1 = RoundRect(new RectangleF(18, 48, 15, 26), 5)) g.FillPath(ink, cup1);
        using (var cup2 = RoundRect(new RectangleF(67, 48, 15, 26), 5)) g.FillPath(ink, cup2);
    }

    private static void FoodDrink(Graphics g, Brush ink, Pen pen)
    {
        // bardak (pipetli) solda, çatal sağda
        g.FillPolygon(ink, new[] { new PointF(22, 40), new PointF(48, 40), new PointF(44, 78), new PointF(26, 78) });
        using (var straw = new Pen(Ink, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(straw, 38, 40, 42, 26);
            g.DrawLine(straw, 42, 26, 50, 24);
        }
        using var fork = new Pen(Ink, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(fork, 68, 46, 68, 80);                      // sap
        foreach (var x in new[] { 61f, 68f, 75f }) g.DrawLine(fork, x, 22, x, 38);   // dişler
        g.DrawArc(fork, 61, 30, 14, 16, 0, 180);              // dişleri sapa bağlayan kavis
    }

    private static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
