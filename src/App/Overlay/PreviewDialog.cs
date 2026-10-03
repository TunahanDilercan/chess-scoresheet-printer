using System.Drawing.Printing;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>
/// Uygulama içi baskı önizleme penceresi (Win11'in native penceresi Win32 uygulamalarda önizleme
/// göstermiyor). Üstte "🖨 Yazdır" (yazıcı seçtirir), "Kapat" ve sayfa gezinme butonları vardır.
/// Notasyon kağıtları ve masa kartları aynı pencereyi kullanır.
/// </summary>
internal static class PreviewDialog
{
    /// <param name="print">Yazıcı seçtirip basar; true = basıldı.</param>
    /// <returns>true = kullanıcı yazdırdı, false = yazdırmadan kapattı.</returns>
    public static bool Show(IWin32Window owner, PrintDocument doc, int pageCount, string hint, Func<IWin32Window, bool> print)
    {
        bool printed = false;
        using var form = new Form
        {
            Text = $"Yazdırma Önizleme — {doc.DocumentName}",
            StartPosition = FormStartPosition.CenterParent,
            WindowState = FormWindowState.Maximized,
            MinimumSize = new Size(700, 500),
            Font = new Font("Segoe UI", 9.75f),
            KeyPreview = true
        };
        if (owner is Form pf) form.Icon = pf.Icon;

        var green = Color.FromArgb(118, 150, 86);
        var preview = new PrintPreviewControl { Dock = DockStyle.Fill, Document = doc, UseAntiAlias = true, AutoZoom = true };

        var bar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(245, 245, 240) };
        var btnPrint = new Button { Text = "🖨  Yazdır", Width = 150, Height = 34, Left = 10, Top = 6, BackColor = green, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
        var btnClose = new Button { Text = "Kapat", Width = 100, Height = 34, Left = 168, Top = 6 };
        var btnPrev = new Button { Text = "◀", Width = 40, Height = 34, Left = 290, Top = 6 };
        var btnNext = new Button { Text = "▶", Width = 40, Height = 34, Left = 334, Top = 6 };
        var lblPage = new Label { AutoSize = true, Left = 382, Top = 14 };
        var lblHint = new Label { AutoSize = true, Left = 500, Top = 14, ForeColor = Color.Gray, Text = hint + " • Ctrl+P yazdır, Esc kapat" };
        bar.Controls.AddRange(new Control[] { btnPrint, btnClose, btnPrev, btnNext, lblPage, lblHint });

        void ShowPage(int p)
        {
            preview.StartPage = Math.Clamp(p, 0, Math.Max(0, pageCount - 1));
            lblPage.Text = $"Sayfa {preview.StartPage + 1} / {pageCount}";
        }
        btnPrev.Click += (_, _) => ShowPage(preview.StartPage - 1);
        btnNext.Click += (_, _) => ShowPage(preview.StartPage + 1);
        ShowPage(0);

        void DoPrint()
        {
            if (!print(form)) return;
            printed = true;
            form.Close();
        }
        btnPrint.Click += (_, _) => DoPrint();
        btnClose.Click += (_, _) => form.Close();
        form.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.P) { e.Handled = true; DoPrint(); }
            else if (e.KeyCode == Keys.Escape) form.Close();
            else if (e.KeyCode is Keys.PageDown or Keys.Right) ShowPage(preview.StartPage + 1);
            else if (e.KeyCode is Keys.PageUp or Keys.Left) ShowPage(preview.StartPage - 1);
        };

        form.Controls.Add(preview); // Fill önce
        form.Controls.Add(bar);     // Top sonra
        // Sayfa ayarları (yön, kağıt) belgeye bağlandıktan sonra ilk çizim bunlarla yenilenir.
        form.Shown += (_, _) => { preview.InvalidatePreview(); btnPrint.Focus(); };
        form.ShowDialog(owner);
        return printed;
    }
}
