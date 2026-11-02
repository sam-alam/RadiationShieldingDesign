using RadiationShieldingDesign.Engine.Nuclear;

namespace RadiationShieldingDesign.Engine.Xray;

/// <summary>
/// One radiation component reaching a barrier's calculation point from one tube position,
/// e.g. "Chest stand – primary" or "Table – secondary". Kerma values follow NCRP 147 (mGy per patient at 1 m).
/// </summary>
/// <param name="Label">Shown in the trace, e.g. "Chest stand – primary".</param>
/// <param name="IsPrimary">Primary beam (use factor and pre-shield apply) or secondary (leakage + scatter).</param>
/// <param name="K1_mGyPerPatient">K_P^1 or K_sec^1 at 1 m for this workload distribution.</param>
/// <param name="PatientsPerWeek">N for this tube position.</param>
/// <param name="UseFactor">U (primary only; use 1 for secondary).</param>
/// <param name="Distance_m">Source to the occupied point (offset beyond the barrier already included).</param>
/// <param name="Alpha">Archer α (1/mm) for this distribution and barrier material.</param>
/// <param name="Beta">Archer β (1/mm).</param>
/// <param name="Gamma">Archer γ.</param>
/// <param name="PreShield_mm">Equivalent thickness of the image receptor/bucky in the path, same material. Used for the primary
/// beam, and for the secondary from the same tube position when that position is aimed at this barrier (scatter toward the
/// wall behind the receptor passes through it too). 0 = none.</param>
/// <param name="PreExistingTransmission">Transmission of existing construction for this component's spectrum.</param>
public sealed record XrayComponent(
    string Label,
    bool IsPrimary,
    double K1_mGyPerPatient,
    double PatientsPerWeek,
    double UseFactor,
    double Distance_m,
    double Alpha,
    double Beta,
    double Gamma,
    double PreShield_mm = 0,
    double PreExistingTransmission = 1);

public sealed class MultiPositionRequest
{
    public string BarrierId { get; init; } = "";
    public double Occupancy { get; init; } = 1.0;
    public double P_mGy_week { get; init; } = 0.02;
    public List<XrayComponent> Components { get; init; } = new();
}

public sealed class ComponentResult
{
    public string Label { get; init; } = "";
    public bool IsPrimary { get; init; }
    /// <summary>Weekly kerma at the point before the new barrier (pre-existing and pre-shield applied), mGy/wk.</summary>
    public double Unshielded_mGy_week { get; init; }
    /// <summary>Weekly kerma at the point with the required new thickness, mGy/wk.</summary>
    public double Shielded_mGy_week { get; init; }
    /// <summary>Share of the total shielded weekly kerma, 0..1.</summary>
    public double Fraction { get; init; }
}

public sealed class MultiPositionResult
{
    public string BarrierId { get; init; } = "";
    /// <summary>New barrier thickness (mm). 0 = none needed; +∞ = not reachable.</summary>
    public double RequiredThickness_mm { get; init; }
    public double Week_mGy_AtRequired { get; init; }
    public List<ComponentResult> Components { get; init; } = new();
    public List<KeyValuePair<string, string>> Trace { get; init; } = new();
}

/// <summary>
/// Sizes one barrier against several tube positions at once (NCRP 147 workload distributions, e.g. chest bucky
/// and floor/other barriers in a rad room, or fluoro + overhead tube in an R&amp;F room).
///
/// Each component keeps its own transmission curve (primary and secondary spectra differ), so the thickness is
/// solved numerically: smallest x ≥ 0 with Σ K_i·N_i·U_i·T/d_i² · TF_pre,i · B_i(x + x_pre,i) ≤ P.
/// With a single component this reproduces the closed-form NCRP 147 inversion used by <see cref="ShieldingCalculator"/>.
/// </summary>
public static class MultiPositionSolver
{
    /// <summary>NCRP 147 / Archer transmission: B(x) = [(1+β/α)·e^(αγx) − β/α]^(−1/γ); x in mm.</summary>
    public static double Transmission(double alpha, double beta, double gamma, double x_mm)
    {
        if (!(alpha > 0) || !(gamma > 0) || !(x_mm > 0)) return 1.0;
        double r = beta / alpha;
        double z = alpha * gamma * x_mm;
        if (z > 600) return Math.Clamp(Math.Pow(1 + r, -1.0 / gamma) * Math.Exp(-alpha * x_mm), 0, 1);
        double inside = (1 + r) * Math.Exp(z) - r;
        if (!(inside > 0)) return 1.0;
        return Math.Clamp(Math.Pow(inside, -1.0 / gamma), 0, 1);
    }

    /// <summary>Weekly kerma at the point before the new barrier, mGy/wk.</summary>
    public static double Unshielded(XrayComponent c, double occupancy)
    {
        if (!(c.Distance_m > 0)) return 0;
        double u = c.IsPrimary ? c.UseFactor : 1.0;
        double k = c.K1_mGyPerPatient * c.PatientsPerWeek * u * occupancy / (c.Distance_m * c.Distance_m);
        double pre = Transmission(c.Alpha, c.Beta, c.Gamma, c.PreShield_mm);
        return Math.Max(0, k * c.PreExistingTransmission * pre);
    }

    private static double ShieldedAt(XrayComponent c, double occupancy, double x_mm)
    {
        if (!(c.Distance_m > 0)) return 0;
        double u = c.IsPrimary ? c.UseFactor : 1.0;
        double k = c.K1_mGyPerPatient * c.PatientsPerWeek * u * occupancy / (c.Distance_m * c.Distance_m);
        double xpre = Math.Max(0, c.PreShield_mm);
        return Math.Max(0, k * c.PreExistingTransmission * Transmission(c.Alpha, c.Beta, c.Gamma, x_mm + xpre));
    }

    public static MultiPositionResult Solve(MultiPositionRequest req)
    {
        var comps = req.Components.Where(c => c.K1_mGyPerPatient > 0 && c.PatientsPerWeek > 0).ToList();
        double Total(double x) => comps.Sum(c => ShieldedAt(c, req.Occupancy, x));

        double x = NuclearBarrierSolver.SolveThickness(Total, req.P_mGy_week);
        double xEval = double.IsFinite(x) ? x : 5000.0;
        double total = Total(xEval);

        var results = comps.Select(c =>
        {
            double s = ShieldedAt(c, req.Occupancy, xEval);
            return new ComponentResult
            {
                Label = c.Label,
                IsPrimary = c.IsPrimary,
                Unshielded_mGy_week = Unshielded(c, req.Occupancy),
                Shielded_mGy_week = s,
                Fraction = total > 0 ? s / total : 0
            };
        }).OrderByDescending(r => r.Shielded_mGy_week).ToList();

        var trace = new List<KeyValuePair<string, string>>
        {
            new("Method", "Multi-position (NCRP 147 workload distributions); thickness solved numerically over all components"),
            new("Occupancy T", req.Occupancy.ToString("G4")),
            new("P (mGy/wk)", req.P_mGy_week.ToString("G4")),
        };
        foreach (var c in comps)
        {
            var r = results.First(z => z.Label == c.Label);
            string pre = c.PreShield_mm > 0 ? $", through receptor {c.PreShield_mm:G3} mm" : "";
            trace.Add(new(c.Label,
                $"K1={c.K1_mGyPerPatient:G4} mGy/pt, N={c.PatientsPerWeek:G4}/wk" +
                (c.IsPrimary ? $", U={c.UseFactor:G3}" : "") +
                $", d={c.Distance_m:G4} m{pre}, existing TF={c.PreExistingTransmission:G4} → unshielded {r.Unshielded_mGy_week:G4} mGy/wk; " +
                $"at required x: {r.Shielded_mGy_week:G4} mGy/wk ({r.Fraction:P0})"));
        }
        trace.Add(new("Total at required x (mGy/wk)", total.ToString("G4")));

        return new MultiPositionResult
        {
            BarrierId = req.BarrierId,
            RequiredThickness_mm = x,
            Week_mGy_AtRequired = total,
            Components = results,
            Trace = trace
        };
    }
}
