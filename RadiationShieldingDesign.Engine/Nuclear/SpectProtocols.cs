namespace RadiationShieldingDesign.Engine.Nuclear;

/// <summary>
/// Starting-point timelines for common cardiac nuclear medicine studies.
/// Activities marked "Kusano 2014 Table 2" come from that paper's hypothetical scan-room workload.
/// All times are typical-workflow ASSUMPTIONS and must be confirmed with the site; every value is editable.
/// Activities set to 0 must be entered by the user (the preset is incomplete on purpose).
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

    public static PatientTimeline Create(string name) => name switch
    {
        Tc1DayRestStress => new PatientTimeline
        {
            Name = name,
            Injections =
            {
                new("Tc-99m", 370, 0),     // rest, Kusano 2014 Table 2 (0.37 GBq)
                new("Tc-99m", 1110, 100),  // stress at peak exercise, Kusano 2014 Table 2 (1.11 GBq)
            },
            Segments =
            {
                new(SpectRegions.Waiting, 0, 45),
                new(SpectRegions.Camera, 45, 65),
                new(SpectRegions.Waiting, 65, 90),
                new(SpectRegions.Stress, 90, 105),
                new(SpectRegions.Waiting, 105, 135),
                new(SpectRegions.Camera, 135, 155),
            },
            Notes =
            {
                "Activities: Kusano & Caldwell 2014, Table 2.",
                "Times are typical assumptions (rest wait 45 min, scans 20 min, stress 15 min, post-stress wait 30 min). Confirm with site."
            }
        },

        Tc2DayPerVisit => new PatientTimeline
        {
            Name = name,
            Injections = { new("Tc-99m", 1110, 10) }, // stress-visit activity, Kusano 2014 Table 2
            Segments =
            {
                new(SpectRegions.Stress, 0, 15),
                new(SpectRegions.Waiting, 15, 45),
                new(SpectRegions.Camera, 45, 65),
            },
            Notes =
            {
                "One visit of a 2-day protocol (stress shown). Count each visit as one patient in the weekly workload.",
                "Activity: Kusano 2014 Table 2 stress value; rest-day activity is often similar. Times are assumptions."
            }
        },

        DualIsotope => new PatientTimeline
        {
            Name = name,
            Injections =
            {
                new("Tl-201", 111, 0),     // rest, Kusano 2014 Table 2 (0.111 GBq)
                new("Tc-99m", 1110, 55),   // stress, Kusano 2014 Table 2
            },
            Segments =
            {
                new(SpectRegions.Waiting, 0, 15),
                new(SpectRegions.Camera, 15, 35),
                new(SpectRegions.Stress, 45, 60),
                new(SpectRegions.Waiting, 60, 75),
                new(SpectRegions.Camera, 75, 95),
            },
            Notes =
            {
                "Activities: Kusano 2014 Table 2. Tl-201 keeps contributing during the stress part (73 h half-life).",
                "Times are typical assumptions. Confirm with site."
            }
        },

        TcPyp => new PatientTimeline
        {
            Name = name,
            Injections = { new("Tc-99m", 0, 0) },
            Segments =
            {
                new(SpectRegions.Waiting, 0, 60),
                new(SpectRegions.Camera, 60, 80),
            },
            Notes = { "ENTER ACTIVITY. Uptake time (60 min here; some sites use 3 h) is an assumption." }
        },

        TcMuga => new PatientTimeline
        {
            Name = name,
            Injections = { new("Tc-99m", 0, 0) },
            Segments =
            {
                new(SpectRegions.Waiting, 0, 10),
                new(SpectRegions.Camera, 10, 40),
            },
            Notes = { "ENTER ACTIVITY. Times are assumptions." }
        },

        I123Mibg => new PatientTimeline
        {
            Name = name,
            Injections = { new("I-123", 0, 0) },
            Segments =
            {
                new(SpectRegions.Camera, 15, 30),   // early images
                new(SpectRegions.Camera, 240, 260), // 4-hour images
            },
            Notes =
            {
                "ENTER ACTIVITY. Assumes the patient leaves the suite between early (15 min) and 4-hour imaging.",
                "If the patient waits on site, add Waiting segments."
            }
        },

        _ => throw new ArgumentException($"Unknown protocol '{name}'.")
    };
}
