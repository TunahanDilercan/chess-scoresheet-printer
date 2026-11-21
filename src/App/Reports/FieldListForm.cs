using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Reports;

/// <summary>
/// Kendi Word şablonuna yazılabilecek her şey: tekil alanlar ({{IL}}, {{BASHAKEM}} …) ve satırı her öğe için
/// çoğalan tablolar ({{sporcu.adi}} …), açıklamaları ve bu turnuvadaki değerleriyle. Çift tıklayınca işaret
/// panoya kopyalanır. İşaretsiz yerlerin (noktalı satır, etiketli boş hücre) de soru olarak geldiği anlatılır.
/// </summary>
public sealed class FieldListForm : Form
{
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
    };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(10, 11, 0, 0) };

    /// <summary>Penceredeki satırlar (test için): işaret, açıklama, bu turnuvadaki değer; başlık satırlarında işaret boş.</summary>
    public IReadOnlyList<(string Marker, string Description, string Value)> Rows { get; }

    public FieldListForm(ReportDraft? draft)
    {
        Text = "Kullanılabilir alanlar — kendi Word şablonunuz için";
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(980, 680);
        MinimumSize = new Size(700, 420);
        this.FitToScreen();
        BackColor = Color.FromArgb(250, 250, 247);

        var help = new Label
        {
            Dock = DockStyle.Top, Height = 92, Padding = new Padding(12, 10, 12, 0), ForeColor = Color.FromArgb(60, 60, 60),
            Text = "Word belgenize işareti olduğu gibi yazın: {{TURNUVA_ADI}} turnuvanın adıyla değişir. Tablolarda bir satıra " +
                   "{{sporcu.adi}} gibi işaretler koyun: o satır her sporcu için çoğalır. İşaret yazmadan da olur: " +
                   "\"İtiraz eden : ………\" gibi noktalı yerler ve \"Etiket | boş hücre\" tablo satırları raporda soru olarak gelir " +
                   "(Turnuva Adı, Tarih, Yeri, Başhakem gibi bilinen etiketler kendiliğinden dolar). Çift tıklayın: işaret panoya kopyalanır."
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Isaret", HeaderText = "Word'e yazılacak işaret", Width = 250 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Aciklama", HeaderText = "Ne yazılır", Width = 330 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Deger", HeaderText = "Bu turnuvada", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns["Aciklama"]!.DefaultCellStyle.WrapMode = _grid.Columns["Deger"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.Columns["Isaret"]!.DefaultCellStyle.Font = new Font("Consolas", 9.5f);

        Rows = Build(draft);
        foreach (var (marker, desc, value) in Rows)
        {
            int i = _grid.Rows.Add(marker, desc, value);
            if (marker.Length == 0)
            {
                _grid.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(232, 240, 224);
                _grid.Rows[i].DefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            }
        }
        _grid.CellDoubleClick += (_, e) => Copy(e.RowIndex);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(10), BackColor = Color.FromArgb(242, 242, 236) };
        var copy = Icons.Set(new Button { Text = "Seçili işareti kopyala", AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.White }, Ico.Copy);
        var copyAll = new Button { Text = "Tüm listeyi kopyala", AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.White };
        var close = new Button { Text = "Kapat", AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.White, DialogResult = DialogResult.OK };
        copy.Click += (_, _) => Copy(_grid.CurrentRow?.Index ?? -1);
        copyAll.Click += (_, _) =>
        {
            Clipboard.SetText(string.Join(Environment.NewLine, Rows.Select(r => r.Marker.Length == 0 ? "" + Environment.NewLine + r.Description : $"{r.Marker}\t{r.Description}")));
            _status.Text = "Tüm liste panoya kopyalandı.";
        };
        bottom.Controls.AddRange(new Control[] { copy, copyAll, close, _status });
        CancelButton = close;

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 8) };
        host.Controls.Add(_grid);
        Controls.Add(host);
        Controls.Add(help);
        Controls.Add(bottom);
    }

    private void Copy(int row)
    {
        if (row < 0 || row >= Rows.Count || Rows[row].Marker.Length == 0) return;
        Clipboard.SetText(Rows[row].Marker);
        _status.Text = $"{Rows[row].Marker} panoya kopyalandı.";
    }

    /// <summary>Tekil alanlar (katalog sırasıyla) ve tablolar (önek başlığı + sütun işaretleri).</summary>
    public static List<(string Marker, string Description, string Value)> Build(ReportDraft? draft)
    {
        var rows = new List<(string, string, string)> { ("", "TEKİL ALANLAR", "") };
        foreach (var (key, (label, hint, _, _)) in ReportBuilder.Catalog)
        {
            var value = draft?.Fields.FirstOrDefault(f => f.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
            rows.Add(($"{{{{{key}}}}}", string.IsNullOrEmpty(hint) ? label : $"{label} — {hint}", value));
        }
        foreach (var (prefix, (title, columns)) in ReportLists.Catalog)
        {
            var sample = draft?.Lists.GetValueOrDefault(prefix);
            rows.Add(("", $"TABLO: {title.ToUpper(new System.Globalization.CultureInfo("tr-TR"))} — satıra {{{{{prefix}.…}}}} yazın, satır her öğe için çoğalır", sample is null ? "" : $"{sample.Count} satır"));
            foreach (var (col, colTitle) in columns)
            {
                var first = sample?.FirstOrDefault(r => r.TryGetValue(col, out var v) && !string.IsNullOrWhiteSpace(v));
                rows.Add(($"{{{{{prefix}.{col}}}}}", colTitle, first is not null ? first[col] : ""));
            }
        }
        return rows;
    }
}
