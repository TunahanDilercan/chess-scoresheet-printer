using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Overlay;

namespace NotasyonOtomasyonu.App.Reports;

/// <summary>
/// Raporun yazdırılmadan önce görüldüğü önizleme penceresi: Word'ün ürettiği PDF'in sayfaları birebir
/// gösterilir. Buradan yazıcıya gönderilir ("doğrudan yazdır" ve yazıcı tercihlerine uyar) ya da PDF olarak kaydedilir.
/// </summary>
public sealed class ReportPreviewForm : Form
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private const int PrintDpi = 300;

    private readonly string _pdfPath;
    private readonly string _defaultName;
    private readonly string _outputDir;
    private readonly List<Bitmap> _pages;
    private readonly List<SizeF> _sizesPt;
    private readonly FlowLayoutPanel _pagesPanel = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.FromArgb(120, 120, 120), Padding = new Padding(20) };
    private readonly Label _lblInfo = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 12, 0, 0) };
    private float _zoom = 1f;   // 1 = sayfa genişliği pencereye sığar

    /// <summary>Kullanıcı yazıcıya gönderdi mi.</summary>
    public bool Printed { get; private set; }
    /// <summary>Kaydedilen PDF yolu (kaydedildiyse).</summary>
    public string? SavedPdf { get; private set; }

    public ReportPreviewForm(string pdfPath, List<Bitmap> pages, List<SizeF> sizesPt, string defaultName, string outputDir, string title)
    {
        _pdfPath = pdfPath;
        _pages = pages;
        _sizesPt = sizesPt;
        _defaultName = defaultName;
        _outputDir = outputDir;
        Text = "Önizleme — " + title;
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(800, 600);
        KeyPreview = true;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(10, 7, 10, 0), BackColor = Color.FromArgb(245, 245, 240), WrapContents = false };
        var print = Btn(PrintRouter.IsSilent ? "🖨  Yazdır (doğrudan)" : "🖨  Yazdır", 170, primary: true);
        var pdf = Btn("📄 PDF Olarak Kaydet", 180);
        var close = Btn("Kapat", 90);
        var zoomOut = Btn("−", 36);
        var zoomFit = Btn("Sayfaya sığdır", 120);
        var zoomIn = Btn("+", 36);
        bar.Controls.AddRange(new Control[] { print, pdf, close, Spacer(), zoomOut, zoomFit, zoomIn, _lblInfo });
        _lblInfo.Text = $"{pages.Count} sayfa • yazıcı: {PrintRouter.TargetName}{(PrintRouter.IsSilent ? " (doğrudan yazdır)" : "")} • Ctrl+P yazdır, Esc kapat";

        print.Click += (_, _) => DoPrint();
        pdf.Click += (_, _) => SavePdf();
        close.Click += (_, _) => Close();
        zoomIn.Click += (_, _) => SetZoom(_zoom * 1.2f);
        zoomOut.Click += (_, _) => SetZoom(_zoom / 1.2f);
        zoomFit.Click += (_, _) => SetZoom(1f);
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.P) { e.Handled = true; DoPrint(); }
            else if (e.KeyCode == Keys.Escape) Close();
            else if (e.Control && e.KeyCode is Keys.Add or Keys.Oemplus) SetZoom(_zoom * 1.2f);
            else if (e.Control && e.KeyCode is Keys.Subtract or Keys.OemMinus) SetZoom(_zoom / 1.2f);
        };

        foreach (var p in pages)
            _pagesPanel.Controls.Add(new PictureBox { Image = p, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, Margin = new Padding(0, 0, 0, 16) });
        _pagesPanel.Resize += (_, _) => Layout();

        Controls.Add(_pagesPanel);
        Controls.Add(bar);
        Shown += (_, _) => { Layout(); print.Focus(); };
    }

    private static Control Spacer() => new Panel { Width = 24, Height = 1 };

    private static Button Btn(string text, int width, bool primary = false)
    {
        var b = new Button
        {
            Text = text, Width = width, Height = 34, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = primary ? Green : Color.White, ForeColor = primary ? Color.White : Color.Black,
            Margin = new Padding(0, 0, 6, 0)
        };
        if (primary) b.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        b.FlatAppearance.BorderColor = primary ? Color.FromArgb(95, 122, 70) : Color.FromArgb(208, 208, 198);
        return b;
    }

    private void SetZoom(float z)
    {
        _zoom = Math.Clamp(z, 0.3f, 4f);
        Layout();
    }

    /// <summary>Sayfaları pencere genişliğine (× yakınlaştırma) göre boyutlandırıp ortalar.</summary>
    private new void Layout()
    {
        int avail = Math.Max(200, _pagesPanel.ClientSize.Width - 60);
        _pagesPanel.SuspendLayout();
        foreach (var (pic, i) in _pagesPanel.Controls.OfType<PictureBox>().Select((p, i) => (p, i)))
        {
            var s = _sizesPt[Math.Min(i, _sizesPt.Count - 1)];
            int w = (int)(Math.Min(avail, 900) * _zoom);
            pic.Size = new Size(w, (int)(w * s.Height / s.Width));
            pic.Margin = new Padding(Math.Max(0, (avail - w) / 2), 0, 0, 16);
        }
        _pagesPanel.ResumeLayout();
    }

    // ================= Yazdırma =================
    private async void DoPrint()
    {
        try
        {
            UseWaitCursor = true;
            bool printed = await PrintPdfAsync(this, _pdfPath, _sizesPt, Text);
            UseWaitCursor = false;
            if (!printed) return;
            Printed = true;
            Close();
        }
        catch (Exception ex)
        {
            UseWaitCursor = false;
            MessageBox.Show(this, "Yazdırılamadı: " + ex.Message, "Yazdır", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// PDF'i yazıcıya gönderir: sayfalar 300 dpi çizilir, kağıdın köşesinden basılır; hedef yazıcı, tepsi,
    /// kopya, yazıcı tercihleri (sessiz mod vb.) ve "doğrudan yazdır" ayarı uygulanır. true = gönderildi.
    /// </summary>
    public static async Task<bool> PrintPdfAsync(IWin32Window owner, string pdfPath, List<SizeF> sizesPt, string docName)
    {
        var pages = await PdfPages.RenderAsync(pdfPath, PrintDpi);
        try
        {
            int index = 0;
            using var doc = new PrintDocument { DocumentName = docName };
            PrintRouter.ApplyTarget(doc);
            doc.OriginAtMargins = false;
            doc.BeginPrint += (_, _) => index = 0;
            doc.QueryPageSettings += (_, e) =>
            {
                var s = sizesPt[Math.Min(index, sizesPt.Count - 1)];
                e.PageSettings.Landscape = s.Width > s.Height;
            };
            doc.PrintPage += (_, e) =>
            {
                var g = e.Graphics!;
                g.PageUnit = GraphicsUnit.Point;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                // (0,0) yazdırılabilir alanın köşesi: sabit kenar boşluğunu telafi et → sayfa kağıdın köşesinden
                g.TranslateTransform(-e.PageSettings.HardMarginX / 100f * 72f, -e.PageSettings.HardMarginY / 100f * 72f);
                var s = sizesPt[Math.Min(index, sizesPt.Count - 1)];
                g.DrawImage(pages[index], 0, 0, s.Width, s.Height);
                index++;
                e.HasMorePages = index < pages.Count;
            };
            return PrintRouter.Print(doc, owner, (d, _) =>
            {
                try { foreach (PaperSize ps in d.PrinterSettings.PaperSizes) if (ps.Kind == PaperKind.A4) { d.DefaultPageSettings.PaperSize = ps; break; } }
                catch (InvalidPrinterException) { }
                return true;
            });
        }
        finally
        {
            foreach (var b in pages) b.Dispose();
        }
    }

    private void SavePdf()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "PDF olarak kaydet", Filter = "PDF belgesi (*.pdf)|*.pdf", FileName = _defaultName, InitialDirectory = _outputDir
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.Copy(_pdfPath, dlg.FileName, overwrite: true);
            SavedPdf = dlg.FileName;
            _lblInfo.Text = "Kaydedildi: " + dlg.FileName;
        }
        catch (IOException ex) { MessageBox.Show(this, "Kaydedilemedi (dosya açık olabilir): " + ex.Message, "PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (var pic in _pagesPanel.Controls.OfType<PictureBox>()) pic.Image = null;
        foreach (var b in _pages) b.Dispose();
        base.OnFormClosed(e);
    }
}
