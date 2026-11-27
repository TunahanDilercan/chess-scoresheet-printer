namespace NotasyonOtomasyonu.App;

/// <summary>
/// Programın pencereleri için temel: ekran ölçeğine (%125, %150 …) uyum.
/// Pencereler kodla, 96 DPI (%100) ölçüsünde piksel olarak kurulur. Windows yazıları ekran ölçeğinde büyütür
/// ama piksel ölçüler (düğme/sütun genişliği, panel, satır yüksekliği) aynı kalırsa yazılar kesilir, düğmeler
/// taşar. Pencere açılırken bütün yerleşim ekranın ölçeğine göre bir kez büyütülür.
/// </summary>
public class AppForm : Form
{
    private bool _scaled;

    protected override void OnLoad(EventArgs e)
    {
        GridSafety.Attach(this);   // tablolarda teknik hata kutusu açılmasın
        if (!_scaled)
        {
            _scaled = true;
            float f = UiScale.FactorFor(this);
            if (f > 1.01f)
            {
                UiScale.Apply(this, f);
                if (StartPosition == FormStartPosition.CenterParent && Owner is not null) CenterToParent();
                else if (StartPosition is FormStartPosition.CenterScreen or FormStartPosition.CenterParent) CenterToScreen();
            }
            // her ölçekte: ekrandan büyük pencere (ör. 1366×768 dizüstü) ekrana sığdırılıp ortalanır
            WindowFit.Fit(this);
            UiScale.FitDockedLabels(this);
            UiScale.FitGridHeaders(this);
        }
        base.OnLoad(e);
    }
}

public static class UiScale
{
    /// <summary>Denemeler için ölçek (ör. 1.5); verilmezse pencerenin bulunduğu ekranın ölçeği.</summary>
    [ThreadStatic] internal static float? Override;

    public static float FactorFor(Control c) => Override ?? c.DeviceDpi / 96f;

    private static float? _system;

    /// <summary>Sistemin (ana ekranın) ölçeği: pencere tanıtıcısı olmadan, ör. kurucuda oluşturulan denetimler için.</summary>
    public static float SystemFactor
    {
        get
        {
            if (Override is { } o) return o;
            if (_system is { } s) return s;
            try { using var g = Graphics.FromHwnd(IntPtr.Zero); _system = g.DpiX / 96f; }
            catch { _system = 1f; }
            return _system.Value;
        }
    }

    /// <summary>%100 ölçüsündeki piksel değerini ekran ölçeğine çevirir (ana pencerede sonradan kurulan denetimler).</summary>
    public static int Px(int v) => (int)Math.Round(v * SystemFactor);

    /// <summary>
    /// %100 ölçüsünde kurulmuş pencereyi <paramref name="f"/> katına büyütür: konum/boyut/kenar boşlukları
    /// (WinForms Scale), tablo sütun genişlikleri ve satır yükseklikleri, en büyük boyut sınırları.
    /// Yazı tipleri zaten Windows tarafından ölçeklendiği için dokunulmaz.
    /// </summary>
    public static void Apply(Form form, float f)
    {
        if (f <= 1.01f) return;
        form.SuspendLayout();
        try
        {
            var maxSizes = new List<(Control C, Size Max)>();
            Walk(form, c => { if (c.MaximumSize != Size.Empty) maxSizes.Add((c, c.MaximumSize)); });
            form.Scale(new SizeF(f, f));
            foreach (var (c, max) in maxSizes)   // en büyük boyut sınırları (ör. satır kaydıran yazılar) da büyüsün
                c.MaximumSize = new Size(max.Width == 0 ? 0 : (int)Math.Round(max.Width * f), max.Height == 0 ? 0 : (int)Math.Round(max.Height * f));
            Walk(form, c =>
            {
                if (c is DataGridView g) ScaleGrid(g, f);
                // Kendi boyunu alan düğme/etiket yalnız büyüyebildiğinden (GrowOnly) ölçekle şişen genişliği
                // yazısına göre geri alınır (yoksa yan yana düğmeler satırdan taşıyordu).
                else if (c.AutoSize && c is ButtonBase or Label && c is not Panel)
                    c.Width = Math.Max(c.MinimumSize.Width, c.GetPreferredSize(Size.Empty).Width);
            });
        }
        finally { form.ResumeLayout(true); }
    }

    private static void ScaleGrid(DataGridView g, float f)
    {
        int S(int v) => (int)Math.Round(v * f);
        foreach (DataGridViewColumn col in g.Columns)
        {
            col.MinimumWidth = Math.Max(2, S(col.MinimumWidth));
            if (col.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill) col.Width = S(col.Width);
        }
        g.RowTemplate.Height = S(g.RowTemplate.Height);
        foreach (DataGridViewRow r in g.Rows) r.Height = S(r.Height);
        // başlık satırı yazıya göre yükseklik alır (sabit yükseklikte büyüyen yazı kesiliyordu)
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        if (g.RowHeadersVisible) g.RowHeadersWidth = S(g.RowHeadersWidth);
    }

    /// <summary>
    /// Üste/alta yaslı açıklama yazıları metne göre uzar: pencere daralınca (küçük ekran, yüksek ölçek)
    /// satır sayısı artar, sabit yükseklikte son satırlar kesiliyordu. Kısalmaz (ilk yüksekliği korur).
    /// </summary>
    public static void FitDockedLabels(Form form)
    {
        Walk(form, c =>
        {
            if (c is not Label l || l.AutoSize || l.AutoEllipsis || l.Dock is not (DockStyle.Top or DockStyle.Bottom)) return;
            int min = l.Height;
            void Fit()
            {
                if (l.Width <= 0 || string.IsNullOrWhiteSpace(l.Text)) return;
                var sz = TextRenderer.MeasureText(l.Text, l.Font, new Size(Math.Max(1, l.ClientSize.Width - l.Padding.Horizontal), 0), TextFormatFlags.WordBreak);
                int h = Math.Max(min, sz.Height + l.Padding.Vertical + 4);
                if (h != l.Height) l.Height = h;
            }
            l.SizeChanged += (_, _) => Fit();
            l.TextChanged += (_, _) => Fit();
            l.FontChanged += (_, _) => Fit();
            Fit();
        });
    }

    /// <summary>
    /// Tablo başlıkları kesilmesin: sabit genişlikli sütun en az başlığı kadar genişler (kalın yazı büyüyünce
    /// birkaç piksel taşıyordu); genişleyemeyen (doldurma) sütunların uzun başlıkları iki satıra iner.
    /// </summary>
    public static void FitGridHeaders(Form form)
    {
        Walk(form, c =>
        {
            if (c is not DataGridView g) return;
            // başlık satırı yazıya göre yükseklik alır: WinForms'un sabit 23 pikseli 9,75 puntoda g/y/ş kuyruklarını kesiyordu
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            var font = g.ColumnHeadersDefaultCellStyle.Font ?? g.Font;
            bool wrap = false;
            foreach (DataGridViewColumn col in g.Columns)
            {
                if (!col.Visible || string.IsNullOrWhiteSpace(col.HeaderText)) continue;
                int need = HeaderWidth(col.HeaderText, font);
                if (need <= col.Width) continue;
                // kısa başlık (tek kelime) için sütun genişler; uzun başlık satır kaydırır
                if (col.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill && (!col.HeaderText.Contains(' ') || need - col.Width <= 24)) col.Width = need;
                else wrap = true;
            }
            if (wrap)
            {
                g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
                g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            }
        });
    }

    /// <summary>Başlık yazısının sütunda kesilmeden durması için gereken genişlik (hücre kenar payı dahil).</summary>
    internal static int HeaderWidth(string text, Font font)
        => TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width + 10;

    private static void Walk(Control c, Action<Control> act)
    {
        foreach (Control k in c.Controls) { act(k); Walk(k, act); }
    }
}
