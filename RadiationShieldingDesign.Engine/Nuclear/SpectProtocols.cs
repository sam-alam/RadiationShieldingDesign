namespace RadiationShieldingDesign.Engine.Nuclear;

/// <summary>
/// User-editable parameters for one cardiac nuclear medicine study. <see cref="SpectProtocols.Build"/> turns it
/// into a <see cref="PatientTimeline"/>. Which fields matter depends on <see cref="Protocol"/>:
/// <list type="bullet">
/// <item>1-day rest/stress: Activity1 = rest Tc-99m, Activity2 = stress Tc-99m; Wait, Scan, Gap (rest scan end → stress start), Stress, PostStressWait.</item>
/// <item>2-day (one visit): Activity1 = Tc-99m; Stress, PostStressWait, Scan.</item>
/// <item>Dual isotope: Activity1 = rest Tl-201, Activity2 = stress Tc-99m; Wait, Scan, Gap, Stress, PostStressWait.</item>
/// <item>PYP / MUGA: Activity1 = Tc-99m; Wait (uptake before scan), Scan.</item>
/// <item>I-123 MIBG: Activity1 = I-123; Wait = early image start, Gap = late image start, Scan (each image).</item>
/// </list>
/// The stress injection is placed two-thirds of the way through the stress period (peak exercise / pharmacologic stress).
/// </summary>
public sealed class SpectStudy
{
    public string Protocol { get; set; } = SpectProtocols.Tc1DayRestStress;
    public double Activity1_MBq { get; set; }
    public double Activity2_MBq { get; set; }
    public double Wait_min { get; set; }
    public double Scan_min { get; set; }
    public double Gap_min { get; set; }
    public double Stress_min { get; set; }
    public double PostStressWait_min { get; set; }
}

/// <summary>
/// Cardiac study timelines. Activities marked "Kusano 2014 Table 2" come from that paper's hypothetical workload.
/// All times are typical-workflow ASSUMPTIONS to be confirmed with the site; every value is editable.
/// PYP, MUGA and MIBG have no default activity on purpose: the user must enter it.
/// </summary>
public static class SpectProtocols
{
    public const string Tc1DayRestStress = "Tc-99m MPI, 1-day rest/stress";
    public const string Tc2DayPerVisit = "Tc-99m MPI, 2-day (one visit)";
    public const string DualIsotope = "Dual isotope (Tl-201 rest + Tc-99m stress)";
    public const string TcPyp = "Tc-99m PYP (cardiac amyloid)";
    public const string TcMuga = "Tc-99m MUGA";
    public const string I123Mibg = "I-123 MIBG";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        Tc1DayRestStress, Tc2DayPerVisit, DualIsotope, TcPyp, TcMuga, I123Mibg
    };

    /// <summary>Nuclide of Activity1 and Activity2 for each protocol (Activity2 null if unused).</summary>
    public static (string Nuclide1, string? Nuclide2) Nuclides(string protocol) => protocol switch
    {
        Tc1DayRestStress => ("Tc-99m", "Tc-99m"),
        Tc2DayPerVisit => ("Tc-99m", null),
        DualIsotope => ("Tl-201", "Tc-99m"),
        TcPyp => ("Tc-99m", null),
        TcMuga => ("Tc-99m", null),
        I123Mibg => ("I-123", null),
        _ => throw new ArgumentException($"Unknown protocol '{protocol}'.")
    };

    public static SpectStudy Defaults(string protocol) => protocol switch
    {
        Tc1DayRestStress => new SpectStudy
        {
            Protocol = protocol, Activity1_MBq = 370, Activity2_MBq = 1110,
            Wait_min = 45, Scan_min = 20, Gap_min = 25, Stress_min = 15, PostStressWait_min = 30
        },
        Tc2DayPerVisit => new SpectStudy
        {
            Protocol = protocol, Activity1_MBq = 1110,
            Scan_min = 20, Stress_min = 15, PostStressWait_min = 30
        },
        DualIsotope => new SpectStudy
        {
            Protocol = protocol, Activity1_MBq = 111, Activity2_MBq = 1110,
            Wait_min = 15, Scan_min = 20, Gap_min = 10, Stress_min = 15, PostStressWait_min = 15
        },
        TcPyp => new SpectStudy { Protocol = protocol, Wait_min = 60, Scan_min = 20 },
        TcMuga => new SpectStudy { Protocol = protocol, Wait_min = 10, Scan_min = 30 },
        I123Mibg => new SpectStudy { Protocol = protocol, Wait_min = 15, Scan_min = 20, Gap_min = 240 },
        _ => throw new ArgumentException($"Unknown protocol '{protocol}'.")
    };

    /// <summary>Default timeline for a protocol (Kusano 2014 activities where available, assumed times).</summary>
    public static PatientTimeline Create(string protocol) => Build(Defaults(protocol));

    public static PatientTimeline Build(SpectStudy s)
    {
        var (nuc1, nuc2) = Nuclides(s.Protocol);
        var tl = new PatientTimeline { Name = s.Protocol };
        double W = Math.Max(0, s.Wait_min), S = Math.Max(0, s.Scan_min), G = Math.Max(0, s.Gap_min),
               St = Math.Max(0, s.Stress_min), P = Math.Max(0, s.PostStressWait_min);

        void Seg(string region, double a, double b) { if (b > a) tl.Segments.Add(new LocationSegment(region, a, b)); }

        switch (s.Protocol)
        {
            case Tc1DayRestStress:
            case DualIsotope:
            {
                tl.Injections.Add(new Injection(nuc1, s.Activity1_MBq, 0));
                Seg(SpectRegions.Waiting, 0, W);
                Seg(SpectRegions.Camera, W, W + S);
                double stressStart = W + S + G;
                Seg(SpectRegions.Waiting, W + S, stressStart);
                Seg(SpectRegions.Stress, stressStart, stressStart + St);
                tl.Injections.Add(new Injection(nuc2!, s.Activity2_MBq, stressStart + St * 2.0 / 3.0));
                Seg(SpectRegions.Waiting, stressStart + St, stressStart + St + P);
                Seg(SpectRegions.Camera, stressStart + St + P, stressStart + St + P + S);
                tl.Notes.Add(s.Protocol == DualIsotope
                    ? "Tl-201 rest, then Tc-99m stress. Tl-201 keeps contributing during the stress part (73 h half-life)."
                    : "Rest and stress on the same day; rest activity is still present during stress imaging.");
                break;
            }
            case Tc2DayPerVisit:
                tl.Injections.Add(new Injection(nuc1, s.Activity1_MBq, St * 2.0 / 3.0));
                Seg(SpectRegions.Stress, 0, St);
                Seg(SpectRegions.Waiting, St, St + P);
                Seg(SpectRegions.Camera, St + P, St + P + S);
                tl.Notes.Add("One visit of a 2-day protocol (stress visit). Count each visit as one patient in patients/week.");
                break;
            case TcPyp:
            case TcMuga:
                tl.Injections.Add(new Injection(nuc1, s.Activity1_MBq, 0));
                Seg(SpectRegions.Waiting, 0, W);
                Seg(SpectRegions.Camera, W, W + S);
                break;
            case I123Mibg:
                tl.Injections.Add(new Injection(nuc1, s.Activity1_MBq, 0));
                Seg(SpectRegions.Camera, W, W + S);
                Seg(SpectRegions.Camera, G, G + S);
                tl.Notes.Add("Assumes the patient leaves the suite between early and late imaging. Add waiting time if not.");
                break;
            default:
                throw new ArgumentException($"Unknown protocol '{s.Protocol}'.");
        }

        var d = Defaults(s.Protocol);
        bool kusanoActivities = s.Activity1_MBq == d.Activity1_MBq && s.Activity2_MBq == d.Activity2_MBq && d.Activity1_MBq > 0;
        tl.Notes.Add(kusanoActivities
            ? "Activities: Kusano & Caldwell 2014, Table 2 defaults."
            : "Activities: user-entered.");
        tl.Notes.Add("Times are user inputs (defaults are typical assumptions). Confirm with the site.");
        return tl;
    }
}
