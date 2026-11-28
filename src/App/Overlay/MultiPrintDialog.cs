using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>Bir yazıcıya düşen kategoriler ve kâğıt sayısı (çoklu yazıcı modu).</summary>
public sealed record PrinterShare(string Printer, IReadOnlyList<string> Categories, int Sheets);

/// <summary>
/// Çoklu yazıcı: "Tüm kategoriler" basılmadan önce hangi kategorinin hangi yazıcıdan çıkacağı gösterilir. Her yazıcının
/// "Tercihler…" düğmesi sürücünün kendi penceresini açar (renkli / siyah-beyaz, sessiz mod, kalite); seçimler kaydedilir.
/// </summary>
public sealed class MultiPrintDialog : AppForm
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);

    public MultiPrintDialog(IReadOnlyList<PrinterShare> shares, PrintConfig config, Action save)
    {
        Text = "Yazıcılara dağıtım — tüm kategoriler";
        Font = new Font("Segoe UI", 9.75f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        BackColor = Color.FromArgb(250, 250, 247);

        int total = shares.Sum(s => s.Sheets);
        var host = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        host.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 12),
            Text = $"{total} kâğıt {shares.Count} yazıcıya gönderilecek. Kategoriler bölünmedi; sırayla ve kâğıt sayısına göre dağıtıldı."
        });

        var table = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 8),
                                           CellBorderStyle = TableLayoutPanelCellBorderStyle.Single, BackColor = Color.White };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        for (int i = 0; i < shares.Count; i++)
        {
            var s = shares[i];
            var who = new Label
            {
                AutoSize = true, MaximumSize = new Size(290, 0), Margin = new Padding(8, 8, 8, 8),
                Text = $"{i + 1}. {s.Printer}\n{string.Join(", ", s.Categories)}", AccessibleName = $"{i + 1}. yazıcı: {s.Printer}"
            };
            var sheets = new Label { AutoSize = true, Margin = new Padding(8, 8, 8, 8), Text = $"{s.Sheets} kâğıt", Font = new Font(Font, FontStyle.Bold) };
            var state = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 2, 0, 0) };
            void Refresh() => state.Text = config.DevModes.ContainsKey(s.Printer) ? "✓ kayıtlı tercihler" : "varsayılan ayarlar";
            Refresh();
            var prefs = new Button { Text = "Tercihler…", AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.White, AccessibleName = $"{s.Printer} tercihleri" };
            prefs.FlatAppearance.BorderColor = Color.FromArgb(208, 208, 198);
            prefs.Click += (_, _) =>
            {
                var result = PrinterPreferences.Edit(this, s.Printer, config.DevModes.GetValueOrDefault(s.Printer));
                if (result is null) return;
                config.DevModes[s.Printer] = result;
                save();
                Refresh();
            };
            var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(8, 6, 8, 6) };
            right.Controls.AddRange(new Control[] { prefs, state });
            table.Controls.Add(who, 0, i);
            table.Controls.Add(sheets, 1, i);
            table.Controls.Add(right, 2, i);
        }
        host.Controls.Add(table);
        host.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 12),
            Text = "Renkli / siyah-beyaz, sessiz mod ve kalite yazıcının \"Tercihler\" penceresinden seçilir; seçimler kaydedilir ve sonraki baskılarda da uygulanır."
        });

        var ok = new Button { Text = $"Yazdır ({total} kâğıt)", AutoSize = true, Height = 38, BackColor = Green, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
        ok.FlatAppearance.BorderColor = GreenDark;
        ok.Font = new Font(Font, FontStyle.Bold);
        var cancel = new Button { Text = "Vazgeç", AutoSize = true, Height = 38, FlatStyle = FlatStyle.Flat, BackColor = Color.White, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right };
        buttons.Controls.AddRange(new Control[] { ok, cancel });
        host.Controls.Add(buttons);
        Controls.Add(host);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
