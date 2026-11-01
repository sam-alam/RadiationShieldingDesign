using System.Globalization;

namespace RadiationShieldingDesign;

/// <summary>Barrier-list summary line and duplicate-barrier support (Ctrl+D).</summary>
public partial class MainViewModel
{
    /// <summary>
    /// One-line overview above the barrier list, e.g. "4 barriers · 2 need shielding · thickest: Corridor 3/64" Lead".
    /// Thicknesses are compared within the same material only, so lead and gypsum aren't ranked against each other.
    /// </summary>
    public string BarrierSummaryText
    {
        get
        {
            var list = Barriers.ToList();
            if (list.Count == 0) return "";

            int needs = list.Count(b => b.RequiredThickness_mm > 0);
            int notSolvable = list.Count(b => double.IsPositiveInfinity(b.RequiredThickness_mm));
            int hourly = list.Count(b => b.GoverningLimit == "Hourly");

            var parts = new List<string>
            {
                $"{list.Count} barrier{(list.Count == 1 ? "" : "s")}",
                needs == 0 ? "none need shielding" : $"{needs} need shielding"
            };
            if (hourly > 0) parts.Add($"{hourly} set by hourly limit");
            if (notSolvable > 0) parts.Add($"{notSolvable} not solvable");

            // Thickest barrier in the most common material among those needing shielding.
            var needing = list.Where(b => b.RequiredThickness_mm > 0 && double.IsFinite(b.RequiredThickness_mm)).ToList();
            if (needing.Count > 0)
            {
                var material = needing.GroupBy(b => b.MaterialName).OrderByDescending(g => g.Count()).First().Key;
                var worst = needing.Where(b => b.MaterialName == material).OrderByDescending(b => b.RequiredThickness_mm).First();
                var where = string.IsNullOrWhiteSpace(worst.ProtectedArea) ? worst.Label : worst.ProtectedArea;
                var thick = string.IsNullOrWhiteSpace(worst.ThicknessShortDisplay)
                    ? worst.RequiredThickness_mm.ToString("0.##", CultureInfo.CurrentCulture) + " mm"
                    : worst.ThicknessShortDisplay;
                parts.Add($"thickest: {where} {thick} {material}".Replace("  ", " "));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Copies the selected barrier (same kind, all settings and pre-existing layers), offset slightly on the canvas.</summary>
    public void DuplicateSelectedBarrier()
    {
        var src = SelectedBarrier;
        if (src == null) return;

        var copy = CloneBarrier(src, src.Kind, "_copy");
        copy.ApplyPreShield = src.ApplyPreShield;
        copy.X1 += 20; copy.Y1 += 20;
        copy.X2 += 20; copy.Y2 += 20;
        copy.RefreshLabelPosition();

        SelectedBarrier = copy;
        ComputeBarrier(copy);
    }
}
