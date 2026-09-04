using System.Text.RegularExpressions;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Özel/detaylı baskı: yalnızca seçili masaların (ör. "3,7" veya "3-5") notasyonunu,
/// oyuncu başına nüsha (varsayılan 2) ile yazdırır ya da PDF üretir.
/// </summary>
public sealed class SpecialPrintForm : Form
{
    private readonly TextBox _boards = new();
    private readonly NumericUpDown _copies = new() { Minimum = 1, Maximum = 10 };
    private readonly HashSet<int> _available;

    /// <summary>Seçilen masa numaraları (boş = tümü).</summary>
    public HashSet<int> Boards { get; private set; } = new();
    public int Copies { get; private set; } = 2;
    /// <summary>true = yazdır, false = PDF.</summary>
    public bool DoPrint { get; private set; } = true;

    public SpecialPrintForm(string title, IReadOnlyList<int> availableBoards, int defaultCopies)
    {
        _available = availableBoards.ToHashSet();

        Text = "🎯 Özel / Detaylı Baskı";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        BackColor = System.Drawing.Color.FromArgb(245, 245, 240);
        Font = new System.Drawing.Font("Segoe UI", 9.75F);
        ClientSize = new System.Drawing.Size(440, 250);

        Controls.Add(new Label { Text = title, AutoSize = false, Size = new System.Drawing.Size(412, 22), Location = new System.Drawing.Point(16, 14), Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold), ForeColor = System.Drawing.Color.FromArgb(95, 122, 70) });

        string range = availableBoards.Count > 0 ? $"Mevcut masalar: {availableBoards.Min()}–{availableBoards.Max()} ({availableBoards.Count} masa)" : "Masa yok";
        Controls.Add(new Label { Text = range, AutoSize = true, Location = new System.Drawing.Point(16, 42) });

        Controls.Add(new Label { Text = "Masa no (ör. 3,7 veya 3-5):", AutoSize = true, Location = new System.Drawing.Point(16, 74) });
        _boards.SetBounds(220, 71, 204, 25);
        Controls.Add(_boards);
        Controls.Add(new Label { Text = "Boş bırakırsanız tüm masalar yazdırılır.", AutoSize = true, ForeColor = System.Drawing.Color.Gray, Location = new System.Drawing.Point(16, 100) });

        Controls.Add(new Label { Text = "Nüsha (oyuncu başına):", AutoSize = true, Location = new System.Drawing.Point(16, 132) });
        _copies.SetBounds(220, 129, 70, 25);
        _copies.Value = Math.Min(_copies.Maximum, Math.Max(_copies.Minimum, defaultCopies));
        Controls.Add(_copies);
        Controls.Add(new Label { Text = "(2 = iki oyuncuya da aynı notasyon)", AutoSize = true, ForeColor = System.Drawing.Color.Gray, Location = new System.Drawing.Point(300, 132) });

        var btnPrint = new Button { Text = "🖨 Yazdır", Size = new System.Drawing.Size(130, 36), Location = new System.Drawing.Point(16, 196), BackColor = System.Drawing.Color.FromArgb(118, 150, 86), ForeColor = System.Drawing.Color.White, FlatStyle = FlatStyle.Flat };
        var btnPdf = new Button { Text = "📄 PDF", Size = new System.Drawing.Size(130, 36), Location = new System.Drawing.Point(154, 196) };
        var btnCancel = new Button { Text = "Vazgeç", Size = new System.Drawing.Size(100, 36), Location = new System.Drawing.Point(324, 196), DialogResult = DialogResult.Cancel };

        btnPrint.Click += (_, _) => Commit(print: true);
        btnPdf.Click += (_, _) => Commit(print: false);
        Controls.Add(btnPrint); Controls.Add(btnPdf); Controls.Add(btnCancel);
        CancelButton = btnCancel;
    }

    private void Commit(bool print)
    {
        if (!TryParseBoards(_boards.Text, out var boards, out var error))
        {
            MessageBox.Show(error, "Geçersiz masa numarası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Boards = boards;
        Copies = (int)_copies.Value;
        DoPrint = print;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>"3,7", "3-5", "1 2 3" gibi girdileri ayrıştırır. Boş = tümü.</summary>
    private bool TryParseBoards(string input, out HashSet<int> boards, out string error)
    {
        boards = new HashSet<int>();
        error = "";
        input = (input ?? "").Trim();
        if (input.Length == 0) return true; // tümü

        foreach (var tokenRaw in input.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = tokenRaw.Trim();
            var range = Regex.Match(token, @"^(\d+)\s*[-–]\s*(\d+)$");
            if (range.Success)
            {
                int a = int.Parse(range.Groups[1].Value), b = int.Parse(range.Groups[2].Value);
                if (a > b) (a, b) = (b, a);
                for (int i = a; i <= b; i++) boards.Add(i);
            }
            else if (int.TryParse(token, out var n)) boards.Add(n);
            else { error = $"Anlaşılmayan giriş: “{token}”. Örnek: 3,7 veya 3-5"; return false; }
        }

        if (_available.Count > 0)
        {
            var missing = boards.Where(b => !_available.Contains(b)).ToList();
            if (missing.Count > 0) { error = "Bu masalar bu kategoride yok: " + string.Join(", ", missing); return false; }
        }
        return true;
    }
}
