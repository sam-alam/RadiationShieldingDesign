namespace RadiationShieldingDesign.Engine.Nuclear;

/// <summary>Standard region ids for a cardiac SPECT suite. Any string works; these are the defaults the presets use.</summary>
public static class SpectRegions
{
    public const string Stress = "Stress";
    public const string Waiting = "Waiting";
    public const string Camera = "Camera";
    public const string Toilet = "Toilet";
}

/// <summary>An administration of activity at a time (minutes from patient arrival).</summary>
public sealed record Injection(string NuclideId, double Activity_MBq, double Time_min);

/// <summary>Where the patient is between two times (minutes from patient arrival).</summary>
public sealed record LocationSegment(string Region, double Start_min, double End_min);

/// <summary>Patient voids at Time_min; activity administered before then is multiplied by FractionRetained.</summary>
public sealed record VoidEvent(double Time_min, double FractionRetained);

/// <summary>One patient's visit: injections, where they spend time, optional voiding.</summary>
public sealed class PatientTimeline
{
    public string Name { get; set; } = "";
    public List<Injection> Injections { get; set; } = new();
    public List<LocationSegment> Segments { get; set; } = new();
    public List<VoidEvent> Voids { get; set; } = new();

    /// <summary>Multiplier for patient self-attenuation. 1.0 = none (conservative, matches point-source constants).</summary>
    public double PatientAttenuationFactor { get; set; } = 1.0;

    /// <summary>Free-text notes shown in the report (e.g. which values are assumptions).</summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>Per-patient source term for one region and one nuclide, at 1 m.</summary>
public sealed class RegionNuclideDose
{
    public string Region { get; init; } = "";
    public string NuclideId { get; init; } = "";
    /// <summary>Integrated dose per patient visit at 1 m, unshielded (µSv).</summary>
    public double Dose_uSv_at1m { get; init; }
    /// <summary>Largest dose per patient in any 1-hour window spent in this region, at 1 m, unshielded (µSv).</summary>
    public double PeakHourDose_uSv_at1m { get; init; }
    public double TimeInRegion_h { get; init; }
}

public static class SpectPatientModel
{
    /// <summary>Activity (MBq) present in the patient at time t (min), for one nuclide, after decay and voiding.</summary>
    public static double ActivityAt(PatientTimeline tl, Nuclide n, double t_min)
    {
        double lambda_per_min = n.Lambda_per_h / 60.0;
        double a = 0;
        foreach (var inj in tl.Injections)
        {
            if (!string.Equals(inj.NuclideId, n.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (t_min < inj.Time_min) continue;
            double f = VoidFactor(tl, inj.Time_min, t_min);
            a += inj.Activity_MBq * Math.Exp(-lambda_per_min * (t_min - inj.Time_min)) * f;
        }
        return a;
    }

    private static double VoidFactor(PatientTimeline tl, double injTime_min, double t_min)
    {
        double f = 1.0;
        foreach (var v in tl.Voids)
            if (v.Time_min > injTime_min && v.Time_min <= t_min)
                f *= Math.Clamp(v.FractionRetained, 0.0, 1.0);
        return f;
    }

    /// <summary>∫ A(t) dt over [a,b] minutes, in MBq·h. Exact: piecewise-analytic between injection and void times.</summary>
    public static double ActivityIntegral_MBq_h(PatientTimeline tl, Nuclide n, double a_min, double b_min)
    {
        if (!(b_min > a_min)) return 0.0;
        double lambda_h = n.Lambda_per_h;

        // Breakpoints where the activity function changes form.
        var cuts = new SortedSet<double> { a_min, b_min };
        foreach (var inj in tl.Injections) if (inj.Time_min > a_min && inj.Time_min < b_min) cuts.Add(inj.Time_min);
        foreach (var v in tl.Voids) if (v.Time_min > a_min && v.Time_min < b_min) cuts.Add(v.Time_min);

        double total = 0;
        var pts = cuts.ToArray();
        for (int i = 0; i < pts.Length - 1; i++)
        {
            double s = pts[i], e = pts[i + 1];
            // Within (s,e) each injection contributes A0·f·exp(-λ(t - t_j)) with constant f.
            foreach (var inj in tl.Injections)
            {
                if (!string.Equals(inj.NuclideId, n.Id, StringComparison.OrdinalIgnoreCase)) continue;
                if (inj.Time_min > s) continue; // not yet administered in this piece
                double mid = 0.5 * (s + e);
                double f = VoidFactor(tl, inj.Time_min, mid);
                double ts_h = (s - inj.Time_min) / 60.0;
                double te_h = (e - inj.Time_min) / 60.0;
                total += inj.Activity_MBq * f * (Math.Exp(-lambda_h * ts_h) - Math.Exp(-lambda_h * te_h)) / lambda_h;
            }
        }
        return total;
    }

    /// <summary>Per-patient dose at 1 m for each region and nuclide in the timeline.</summary>
    public static List<RegionNuclideDose> Compute(PatientTimeline tl, NuclideLibrary lib)
    {
        var result = new List<RegionNuclideDose>();
        var nuclideIds = tl.Injections.Select(i => i.NuclideId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var regions = tl.Segments.Select(s => s.Region).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        double paf = Math.Clamp(tl.PatientAttenuationFactor, 0.0, 1.0);

        foreach (var region in regions)
        {
            var segs = tl.Segments.Where(s => string.Equals(s.Region, region, StringComparison.OrdinalIgnoreCase)).ToList();
            double time_h = segs.Sum(s => Math.Max(0, s.End_min - s.Start_min)) / 60.0;

            foreach (var id in nuclideIds)
            {
                var n = lib.Get(id);
                double gamma = n.DoseRateConst_uSv_m2_per_MBq_h;

                double integral = segs.Sum(s => ActivityIntegral_MBq_h(tl, n, s.Start_min, s.End_min));

                // Peak 1-hour window inside this region. Activity only drops between injections,
                // so the worst window starts at a segment start or an injection time.
                var starts = segs.Select(s => s.Start_min)
                    .Concat(tl.Injections.Where(i => segs.Any(s => i.Time_min >= s.Start_min && i.Time_min < s.End_min)).Select(i => i.Time_min))
                    .Distinct();
                double peak = 0;
                foreach (var w0 in starts)
                {
                    double w1 = w0 + 60.0;
                    double inWindow = segs.Sum(s =>
                    {
                        double a = Math.Max(s.Start_min, w0), b = Math.Min(s.End_min, w1);
                        return b > a ? ActivityIntegral_MBq_h(tl, n, a, b) : 0.0;
                    });
                    peak = Math.Max(peak, inWindow);
                }

                result.Add(new RegionNuclideDose
                {
                    Region = region,
                    NuclideId = n.Id,
                    Dose_uSv_at1m = gamma * integral * paf,
                    PeakHourDose_uSv_at1m = gamma * peak * paf,
                    TimeInRegion_h = time_h
                });
            }
        }
        return result;
    }
}
