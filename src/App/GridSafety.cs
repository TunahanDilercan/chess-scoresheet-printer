using System.Runtime.CompilerServices;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Tablolarda biçimlendirme/değer hatası olursa WinForms'un İngilizce-teknik "DataGridView Varsayılan Hata
/// İletişim Kutusu" açılmasın: hata yutulur ve kayda yazılır (hücre boş görünür, program çalışmaya devam eder).
/// Pencere açılırken içindeki bütün tablolara ve sonradan eklenenlere bağlanır.
/// </summary>
internal static class GridSafety
{
    private static readonly ConditionalWeakTable<Control, object> Attached = new();

    /// <summary>Son yakalanan tablo hatası (tanılama ve testler için).</summary>
    public static string? LastError { get; private set; }

    public static void Attach(Control root)
    {
        if (Attached.TryGetValue(root, out _)) return;
        Attached.Add(root, root);
        if (root is DataGridView grid) grid.DataError += OnDataError;
        root.ControlAdded += (_, e) => { if (e.Control is { } c) Attach(c); };
        foreach (Control c in root.Controls) Attach(c);
    }

    private static void OnDataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
        var grid = sender as DataGridView;
        var col = grid is not null && e.ColumnIndex >= 0 && e.ColumnIndex < grid.Columns.Count ? grid.Columns[e.ColumnIndex].Name : "?";
        LastError = $"{grid?.AccessibleName ?? grid?.Name} satır {e.RowIndex}, sütun {col} ({e.Context}): {e.Exception?.Message}";
        System.Diagnostics.Trace.WriteLine("Tablo hatası: " + LastError);
    }
}
