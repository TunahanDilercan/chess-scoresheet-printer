using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace NotasyonOtomasyonu.UiTests;

/// <summary>Tablo hücrelerinde sığmayan (kesilen, "…" ile biten) yazılar.</summary>
internal static class CellProbe
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);

    public static IEnumerable<string> TruncatedCells(AutomationElement window, IntPtr hwnd)
    {
        float dpi = 96;
        try { dpi = GetDpiForWindow(hwnd); } catch { }
        using var font = new System.Drawing.Font("Segoe UI", 9f * dpi / 96f, System.Drawing.GraphicsUnit.Point);
        using var bmp = new System.Drawing.Bitmap(1, 1);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        var found = new List<string>();
        AutomationElement[] grids;
        try { grids = window.FindAllDescendants(c => c.ByControlType(ControlType.DataGrid).Or(c.ByControlType(ControlType.Table))); } catch { yield break; }
        foreach (var grid in grids)
        {
            string gridName = grid.Properties.Name.ValueOrDefault ?? "";
            int total = 0, cut = 0;
            var examples = new List<string>();
            AutomationElement[] cells;
            try { cells = grid.FindAllDescendants(); } catch { continue; }
            foreach (var cell in cells)
            {
                string? text = null;
                try { if (cell.Patterns.Value.TryGetPattern(out var vp)) text = vp.Value.ValueOrDefault; } catch { }
                if (string.IsNullOrWhiteSpace(text) || text is "True" or "False") continue;
                System.Drawing.Rectangle r;
                try { if (cell.Properties.IsOffscreen.ValueOrDefault) continue; r = cell.Properties.BoundingRectangle.ValueOrDefault; } catch { continue; }
                if (r.Width <= 0) continue;
                total++;
                var need = System.Windows.Forms.TextRenderer.MeasureText(g, text, font, System.Drawing.Size.Empty, System.Windows.Forms.TextFormatFlags.NoPadding).Width + 6;
                if (need > r.Width) { cut++; if (examples.Count < 4) examples.Add($"'{text}' ({need} > {r.Width} px)"); }
            }
            found.Add($"(bilgi) tablo '{gridName}': {total} hücre ölçüldü, {cut} kesik");
            if (cut > 0) found.Add($"**kesik hücre:** tablo '{gridName}': {cut}/{total} hücre sığmıyor — ör. {string.Join(", ", examples)}");
        }
        foreach (var f in found) yield return f;
    }
}
