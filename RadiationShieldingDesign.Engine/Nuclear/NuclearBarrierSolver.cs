namespace RadiationShieldingDesign.Engine.Nuclear;

/// <summary>One patient type and how many of them per week.</summary>
public sealed record ProtocolWorkload(PatientTimeline Timeline, double PatientsPerWeek);

/// <summary>A source region seen by the barrier's calculation point.</summary>
/// <param name="Distance_m">Source to occupied point, m (include the usual 0.3 m beyond the wall).</param>
/// <param name="MaxSimultaneousPatients">Most patients in this region at once; used only for the hourly check.</param>
/// <param name="PatientsPerHour">
/// Most DISTINCT patients that can be in this region during any one clock hour (turnover). 0 = not specified,
/// in which case the hourly check uses <paramref name="MaxSimultaneousPatients"/> only. For a room with fast
/// turnover (stress/injection: 4 patients × 15 min in an hour) this is what sets the hourly dose, not how many
/// are present at once. The hourly dose uses max(MaxSimultaneousPatients, PatientsPerHour) × the largest
/// per-patient 1-hour dose in the region — a conservative envelope (it credits no decay between patients).
/// </param>
public sealed record RegionExposure(string Region, double Distance_m, double MaxSimultaneousPatients = 1, double PatientsPerHour = 0);

public sealed record ShieldLayer(string Material, double Thickness_mm);

public sealed class NuclearBarrierRequest
{
    public string BarrierId { get; init; } = "";
    public List<ProtocolWorkload> Workloads { get; init; } = new();
    public List<RegionExposure> Regions { get; init; } = new();
    public double Occupancy { get; init; } = 1.0;

    /// <summary>Weekly design goal, mSv/wk (numerically = mGy/wk as used elsewhere in the app). 0.1 controlled, 0.02 uncontrolled.</summary>
    public double P_week_mSv { get; init; } = 0.02;

    /// <summary>Public dose limit in any one hour, mSv (NRC / 10 NYCRR 16: 0.02). Set ≤ 0 to skip the hourly check (e.g. controlled areas).</summary>
    public double P_hour_mSv { get; init; } = 0.02;

    /// <summary>Material of the NEW shielding to be sized.</summary>
    public string Material { get; init; } = "Lead";
    public List<ShieldLayer> PreExisting { get; init; } = new();
}

public sealed class NuclideContribution
{
    public string NuclideId { get; init; } = "";
    public double Unshielded_week_mSv { get; init; }
    public double Unshielded_hour_mSv { get; init; }
    public double PreExistingTransmission { get; init; }
    public double Shielded_week_mSv { get; init; }
    public double Shielded_hour_mSv { get; init; }
}

/// <summary>How much of the barrier's weekly dose one protocol (study) contributes.</summary>
public sealed class WorkloadContribution
{
    public string Name { get; init; } = "";
    public double PatientsPerWeek { get; init; }
    /// <summary>Weekly dose at the point from this protocol after all shielding (pre-existing + required), mSv/wk.</summary>
    public double Shielded_week_mSv { get; init; }
    /// <summary>Share of the barrier's total shielded weekly dose, 0..1.</summary>
    public double Fraction { get; init; }
}

public sealed class NuclearBarrierResult
{
    public string BarrierId { get; init; } = "";
    /// <summary>New shielding thickness needed in mm. 0 = none needed. +∞ = not achievable with this material.</summary>
    public double RequiredThickness_mm { get; init; }
    public double ThicknessForWeekly_mm { get; init; }
    public double ThicknessForHourly_mm { get; init; }
    /// <summary>"Weekly", "Hourly", or "None" (no shielding needed).</summary>
    public string Governing { get; init; } = "None";
    public double Week_mSv_AtRequired { get; init; }
    public double Hour_mSv_AtRequired { get; init; }
    public List<NuclideContribution> Contributions { get; init; } = new();
    /// <summary>Per-protocol share of the shielded weekly dose, largest first.</summary>
    public List<WorkloadContribution> ByWorkload { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public List<KeyValuePair<string, string>> Trace { get; init; } = new();
}

/// <summary>
/// Sizes a barrier against nuclear-medicine patient sources with one or more nuclides.
/// Transmission is applied per nuclide (nuclides attenuate differently), and the thickness is
/// found numerically: the smallest x with Σ_i D_i·TF_pre,i·TF_i(x) ≤ P, for the weekly goal and,
/// separately, the hourly limit. The larger thickness governs.
/// </summary>
public static class NuclearBarrierSolver
{
    private const double MaxSearch_mm = 5000.0;
    private const double Tol_mm = 1e-4;

    public static NuclearBarrierResult Solve(NuclearBarrierRequest req, NuclideLibrary lib)
    {
        var warnings = new List<string>();
        var trace = new List<KeyValuePair<string, string>>();
        void T(string k, string v) => trace.Add(new(k, v));

        // ---- 0) Validate. An invalid design must not produce a thickness (a quiet 0 mm is worse than an error). ----
        Validate(req);

        // ---- 1) Per-patient source terms, per workload ----
        var perWorkload = req.Workloads
            .Where(w => w.PatientsPerWeek > 0)
            .Select(w => (w, doses: SpectPatientModel.Compute(w.Timeline, lib)))
            .ToList();

        var nuclideIds = perWorkload.SelectMany(p => p.doses.Select(d => d.NuclideId))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // ---- 2) Unshielded weekly and hourly dose at the point, per nuclide ----
        var weekly = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var hourly = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in nuclideIds) { weekly[id] = 0; hourly[id] = 0; }
        // Same weekly dose, split by protocol: wlWeekly[workload index][nuclide]
        var wlWeekly = perWorkload.Select(pw => nuclideIds.ToDictionary(id => id, id => 0.0, StringComparer.OrdinalIgnoreCase)).ToList();

        foreach (var rx in req.Regions)
        {
            double inv_r2 = 1.0 / (rx.Distance_m * rx.Distance_m);
            // Hourly multiplier: concurrent patients OR distinct patients cycling through within the hour,
            // whichever is larger. Concurrency alone misses turnover (4 × 15-min visits in one hour = 4 patients' worth).
            double nHour = Math.Max(Math.Max(1.0, rx.MaxSimultaneousPatients), rx.PatientsPerHour);
            T($"Hourly patients ({rx.Region})", rx.PatientsPerHour > 0
                ? $"max({rx.MaxSimultaneousPatients:G4} at once, {rx.PatientsPerHour:G4} per hour) = {nHour:G4}"
                : $"{nHour:G4} at once (patients/hour not set: no turnover credit beyond concurrency)");

            foreach (var id in nuclideIds)
            {
                double wk_uSv = 0, peakHourPerPt_uSv = 0, shortestStay_h = double.PositiveInfinity;
                for (int wi = 0; wi < perWorkload.Count; wi++)
                {
                    var (w, doses) = perWorkload[wi];
                    var d = doses.FirstOrDefault(x =>
                        string.Equals(x.Region, rx.Region, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.NuclideId, id, StringComparison.OrdinalIgnoreCase));
                    if (d == null) continue;
                    double part = d.Dose_uSv_at1m * w.PatientsPerWeek;
                    wk_uSv += part;
                    wlWeekly[wi][id] += part * inv_r2 * req.Occupancy / 1000.0;
                    peakHourPerPt_uSv = Math.Max(peakHourPerPt_uSv, d.PeakHourDose_uSv_at1m);
                    shortestStay_h = Math.Min(shortestStay_h, d.TimeInRegion_h);
                }
                // Weekly: occupancy applies. Hourly: no occupancy factor (limit is on dose in any one hour at the point).
                weekly[id] += wk_uSv * inv_r2 * req.Occupancy / 1000.0;
                hourly[id] += peakHourPerPt_uSv * nHour * inv_r2 / 1000.0;

                if (req.P_hour_mSv > 0 && rx.PatientsPerHour <= 0 && shortestStay_h < 1.0 && peakHourPerPt_uSv > 0)
                    warnings.Add($"Region '{rx.Region}': patients stay {shortestStay_h * 60:G3} min, so more than {nHour:G4} different patient(s) can pass through in one hour. " +
                                 "Set patients/hour for this region or the hourly check may be low.");
            }
        }

        // ---- 3) Transmission functions per nuclide ----
        var preTf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var newTf = new Dictionary<string, Func<double, double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in nuclideIds)
        {
            var n = lib.Get(id);
            double pre = 1.0;
            foreach (var layer in req.PreExisting.Where(l => l.Thickness_mm > 0))
            {
                if (n.TryGetMaterial(layer.Material, out var mt))
                {
                    pre *= mt.Transmission(layer.Thickness_mm);
                    if (mt.LowConfidence) warnings.Add($"{id} in {layer.Material}: low-confidence fit. {mt.Note}");
                }
                else
                {
                    warnings.Add($"No {id} transmission data for pre-existing '{layer.Material}'; that layer is ignored for {id} (conservative).");
                }
            }
            preTf[id] = pre;

            if (n.TryGetMaterial(req.Material, out var newMat))
            {
                newTf[id] = newMat.Transmission;
                if (newMat.LowConfidence) warnings.Add($"{id} in {req.Material}: low-confidence fit. {newMat.Note}");
                if (newMat.Note != null && !newMat.LowConfidence) T($"{id} in {req.Material}", newMat.Note);
            }
            else
            {
                newTf[id] = _ => 1.0;
                warnings.Add($"No {id} transmission data for '{req.Material}'; the new barrier is assumed to give {id} no attenuation.");
            }
        }

        double WeekAt(double x) => nuclideIds.Sum(id => weekly[id] * preTf[id] * newTf[id](x));
        double HourAt(double x) => nuclideIds.Sum(id => hourly[id] * preTf[id] * newTf[id](x));

        // ---- 4) Solve each limit separately ----
        double xWeek = SolveThickness(WeekAt, req.P_week_mSv);
        double xHour = req.P_hour_mSv > 0 ? SolveThickness(HourAt, req.P_hour_mSv) : 0.0;
        double xReq = Math.Max(xWeek, xHour);

        string governing = xReq <= 0 ? "None" : (xHour > xWeek ? "Hourly" : "Weekly");
        if (double.IsPositiveInfinity(xReq))
            warnings.Add($"Limit cannot be met with {req.Material} alone up to {MaxSearch_mm} mm. Check inputs or choose another material.");

        double xEval = double.IsFinite(xReq) ? xReq : MaxSearch_mm;

        var contributions = nuclideIds.Select(id => new NuclideContribution
        {
            NuclideId = id,
            Unshielded_week_mSv = weekly[id],
            Unshielded_hour_mSv = hourly[id],
            PreExistingTransmission = preTf[id],
            Shielded_week_mSv = weekly[id] * preTf[id] * newTf[id](xEval),
            Shielded_hour_mSv = hourly[id] * preTf[id] * newTf[id](xEval),
        }).ToList();

        T("Method", "Per-nuclide broad-beam transmission; thickness solved numerically (bisection)");
        T("Dose constants", "Point source in air, no patient self-attenuation unless set per protocol");
        T("Units", "µSv treated as µGy (photons, wR = 1); design goals in mSv numerically equal mGy");
        T("Occupancy T", req.Occupancy.ToString("G4"));
        T("P weekly (mSv/wk)", req.P_week_mSv.ToString("G4"));
        T("P hourly (mSv in any hour)", req.P_hour_mSv > 0 ? req.P_hour_mSv.ToString("G4") : "not checked");
        foreach (var c in contributions)
            T($"{c.NuclideId} unshielded", $"{c.Unshielded_week_mSv:G4} mSv/wk, {c.Unshielded_hour_mSv:G4} mSv/h peak, pre-existing TF {c.PreExistingTransmission:G4}");
        T("Thickness for weekly goal (mm)", Fmt(xWeek));
        T("Thickness for hourly limit (mm)", req.P_hour_mSv > 0 ? Fmt(xHour) : "n/a");
        T("Governing", governing);

        // ---- Per-protocol share of the weekly dose at the required thickness ----
        var shieldedByWl = perWorkload.Select((p, wi) => (p.w,
            dose: nuclideIds.Sum(id => wlWeekly[wi][id] * preTf[id] * newTf[id](xEval)))).ToList();
        double totalShielded = shieldedByWl.Sum(x => x.dose);
        var byWorkload = shieldedByWl
            .Select(x => new WorkloadContribution
            {
                Name = x.w.Timeline.Name,
                PatientsPerWeek = x.w.PatientsPerWeek,
                Shielded_week_mSv = x.dose,
                Fraction = totalShielded > 0 ? x.dose / totalShielded : 0
            })
            .OrderByDescending(c => c.Fraction)
            .ToList();
        foreach (var c in byWorkload)
            T($"Share: {c.Name}", $"{c.Fraction:P0} of weekly dose ({c.Shielded_week_mSv:G3} mSv/wk shielded, {c.PatientsPerWeek:G4} pts/wk)");

        return new NuclearBarrierResult
        {
            BarrierId = req.BarrierId,
            RequiredThickness_mm = xReq,
            ThicknessForWeekly_mm = xWeek,
            ThicknessForHourly_mm = xHour,
            Governing = governing,
            Week_mSv_AtRequired = WeekAt(xEval),
            Hour_mSv_AtRequired = HourAt(xEval),
            Contributions = contributions,
            ByWorkload = byWorkload,
            Warnings = warnings.Distinct().ToList(),
            Trace = trace
        };
    }

    /// <summary>
    /// Rejects requests that cannot describe a real design. Throws <see cref="ArgumentException"/> listing every
    /// problem found, so the caller shows an error instead of a thickness. Zero patients/week disables a workload
    /// (allowed); a workload that IS counted must have positive, finite activities and a sane timeline.
    /// </summary>
    public static void Validate(NuclearBarrierRequest req)
    {
        var errs = new List<string>();
        static bool Bad(double v) => double.IsNaN(v) || double.IsInfinity(v);

        if (Bad(req.Occupancy) || req.Occupancy <= 0 || req.Occupancy > 1)
            errs.Add($"Occupancy T must be in (0, 1]; got {req.Occupancy}.");
        if (Bad(req.P_week_mSv) || req.P_week_mSv <= 0)
            errs.Add($"Weekly design goal must be > 0 mSv/wk; got {req.P_week_mSv}.");
        if (Bad(req.P_hour_mSv) || req.P_hour_mSv < 0)
            errs.Add($"Hourly limit must be ≥ 0 mSv (0 = not checked); got {req.P_hour_mSv}.");
        if (string.IsNullOrWhiteSpace(req.Material))
            errs.Add("No barrier material selected.");

        if (req.Regions.Count == 0)
            errs.Add("The barrier sees no source region.");
        foreach (var rx in req.Regions)
        {
            if (Bad(rx.Distance_m) || rx.Distance_m <= 0) errs.Add($"Region '{rx.Region}': distance must be > 0 m; got {rx.Distance_m}.");
            if (Bad(rx.MaxSimultaneousPatients) || rx.MaxSimultaneousPatients < 0) errs.Add($"Region '{rx.Region}': max patients at once must be ≥ 0; got {rx.MaxSimultaneousPatients}.");
            if (Bad(rx.PatientsPerHour) || rx.PatientsPerHour < 0) errs.Add($"Region '{rx.Region}': patients/hour must be ≥ 0; got {rx.PatientsPerHour}.");
        }

        foreach (var layer in req.PreExisting)
            if (Bad(layer.Thickness_mm) || layer.Thickness_mm < 0)
                errs.Add($"Pre-existing '{layer.Material}': thickness must be ≥ 0 mm; got {layer.Thickness_mm}.");

        foreach (var w in req.Workloads)
        {
            string name = string.IsNullOrWhiteSpace(w.Timeline.Name) ? "(unnamed)" : w.Timeline.Name;
            if (Bad(w.PatientsPerWeek) || w.PatientsPerWeek < 0) { errs.Add($"'{name}': patients/week must be a number ≥ 0; got {w.PatientsPerWeek}."); continue; }
            if (w.PatientsPerWeek == 0) continue; // disabled: not counted, so its fields do not matter

            if (w.Timeline.Injections.Count == 0) errs.Add($"'{name}': no injection.");
            foreach (var inj in w.Timeline.Injections)
                if (Bad(inj.Activity_MBq) || inj.Activity_MBq <= 0 || Bad(inj.Time_min) || inj.Time_min < 0)
                    errs.Add($"'{name}': {inj.NuclideId} activity must be > 0 MBq at a time ≥ 0 min; got {inj.Activity_MBq} MBq at {inj.Time_min} min.");
            if (w.Timeline.Segments.Count == 0) errs.Add($"'{name}': the patient is never in a room.");
            foreach (var s in w.Timeline.Segments)
                if (Bad(s.Start_min) || Bad(s.End_min) || s.Start_min < 0 || !(s.End_min > s.Start_min))
                    errs.Add($"'{name}': time in '{s.Region}' from {s.Start_min} to {s.End_min} min is not a valid interval.");
            foreach (var v in w.Timeline.Voids)
                if (Bad(v.Time_min) || v.Time_min < 0 || Bad(v.FractionRetained) || v.FractionRetained < 0 || v.FractionRetained > 1)
                    errs.Add($"'{name}': void at {v.Time_min} min retaining {v.FractionRetained} is invalid.");
            if (Bad(w.Timeline.PatientAttenuationFactor) || w.Timeline.PatientAttenuationFactor <= 0 || w.Timeline.PatientAttenuationFactor > 1)
                errs.Add($"'{name}': patient attenuation factor must be in (0, 1]; got {w.Timeline.PatientAttenuationFactor}.");
        }

        if (errs.Count > 0)
            throw new ArgumentException("Invalid shielding input:\n• " + string.Join("\n• ", errs));
    }

    /// <summary>Smallest x ≥ 0 with f(x) ≤ P, for f non-increasing. +∞ if not reached by MaxSearch_mm.</summary>
    public static double SolveThickness(Func<double, double> f, double P)
    {
        if (!(P > 0)) return double.PositiveInfinity;
        if (f(0) <= P) return 0.0;

        double lo = 0, hi = 1.0;
        while (f(hi) > P)
        {
            lo = hi;
            hi *= 2;
            if (hi > MaxSearch_mm)
            {
                if (f(MaxSearch_mm) > P) return double.PositiveInfinity;
                hi = MaxSearch_mm;
                break;
            }
        }
        while (hi - lo > Tol_mm)
        {
            double mid = 0.5 * (lo + hi);
            if (f(mid) > P) lo = mid; else hi = mid;
        }
        return hi;
    }

    private static string Fmt(double x) => double.IsPositiveInfinity(x) ? "not achievable" : x.ToString("F3");
}
