using CommunityToolkit.Mvvm.ComponentModel;
using RadiationShieldingDesign.Engine.Nuclear;
using System.Collections.ObjectModel;
using System.Globalization;

namespace RadiationShieldingDesign;

/// <summary>
/// Cardiac SPECT (nuclear medicine) rooms. Mirrors the PET pattern: room categories, a parameters card,
/// a per-barrier source-region selector, and a compute branch, but the dose and thickness come from
/// <see cref="NuclearBarrierSolver"/> (per-nuclide transmission, weekly goal + hourly public limit).
/// </summary>
public partial class MainViewModel
{
    // ===== Room categories =====
    public const string SpectCameraRoom = "SPECT Camera room";
    public const string SpectWaitingRoom = "SPECT Hot waiting room";
    public const string SpectStressRoom = "SPECT Stress/Injection room";
    public const string SpectSuite = "Cardiac SPECT Suite (all regions)";

    public static IReadOnlyList<string> SpectRoomCategories { get; } =
        new[] { SpectCameraRoom, SpectWaitingRoom, SpectStressRoom, SpectSuite };

    public bool IsSPECT => SelectedRoomCategory is SpectCameraRoom or SpectWaitingRoom or SpectStressRoom or SpectSuite;
    public bool IsSpectSuite => SelectedRoomCategory == SpectSuite;

    /// <summary>PET or SPECT: patient is the source (no NCRP workload tables, use factor 1).</summary>
    public bool IsNuclear => IsPET || IsSPECT;

    private static bool IsSpectCategory(string? cat) =>
        cat is SpectCameraRoom or SpectWaitingRoom or SpectStressRoom or SpectSuite;

    // ===== Materials =====
    public ObservableCollection<string> MaterialNamesSpect { get; } = new()
    {
        "Lead", "Concrete", "Lite Concrete", "Gypsum Wallboard", "Steel", "Glass"
    };

    public ObservableCollection<string> PreexistingMaterialNamesSpect { get; } = new()
    {
        "None", "Lead", "Concrete", "Lite Concrete", "Gypsum Wallboard", "Steel", "Glass", "Wood"
    };

    /// <summary>Maps app material names (incl. x-ray pre-existing names) to nuclide-library names.</summary>
    private static string? MapSpectMaterial(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return name.Trim() switch
        {
            "None" => null,
            "Lead" or "Lead-Eq" => "Lead",
            "Concrete" or "Std Concrete" => "Concrete",
            "Lite Concrete" => "Lite Concrete",
            "Gypsum" or "Gypsum Wallboard" => "Gypsum Wallboard",
            "Steel" or "Iron" => "Steel",
            "Glass" or "Lead Glass" => "Glass",
            var other => other   // e.g. "Wood": solver warns that it has no data and gives no credit
        };
    }

    private void EnforceSpectMaterialsIfNeeded()
    {
        if (!IsSPECT) return;
        foreach (var b in Barriers)
        {
            if (!MaterialNamesSpect.Contains(b.MaterialName)) b.MaterialName = "Lead";
            foreach (var pl in b.PreexistingLayers)
            {
                var mapped = MapSpectMaterial(pl.MaterialName) ?? "None";
                if (PreexistingMaterialNamesSpect.Contains(mapped)) pl.MaterialName = mapped;
            }
        }
    }

    // ===== Study inputs (SPECT Parameters card) =====
    public ObservableCollection<SpectStudyViewModel> SpectStudies { get; } = CreateDefaultSpectStudies();

    private static ObservableCollection<SpectStudyViewModel> CreateDefaultSpectStudies()
    {
        var list = new ObservableCollection<SpectStudyViewModel>(SpectProtocols.All.Select(p => new SpectStudyViewModel(p)));
        list[0].IsEnabled = true;            // Tc-99m 1-day rest/stress
        list[0].PatientsPerWeekText = "40";
        return list;
    }

    private bool _spectStudiesHooked;
    private void HookSpectStudies()
    {
        if (_spectStudiesHooked) return;
        _spectStudiesHooked = true;
        foreach (var s in SpectStudies) s.InputChanged += OnSpectInputChanged;
    }

    /// <summary>Most patients present at once in each region (hourly check only).</summary>
    [ObservableProperty] private string spectMaxPtsWaitingText = "3";
    [ObservableProperty] private string spectMaxPtsCameraText = "1";
    [ObservableProperty] private string spectMaxPtsStressText = "1";

    /// <summary>Most DIFFERENT patients that can pass through each region in any one clock hour (turnover). Blank = use at-once count.</summary>
    [ObservableProperty] private string spectPtsPerHourWaitingText = "";
    [ObservableProperty] private string spectPtsPerHourCameraText = "";
    [ObservableProperty] private string spectPtsPerHourStressText = "";

    /// <summary>Public dose limit in any one hour (mSv), applied to uncontrolled barriers.</summary>
    [ObservableProperty] private string spectHourlyLimitText = "0.02";

    partial void OnSpectMaxPtsWaitingTextChanged(string value) => OnSpectInputChanged();
    partial void OnSpectMaxPtsCameraTextChanged(string value) => OnSpectInputChanged();
    partial void OnSpectMaxPtsStressTextChanged(string value) => OnSpectInputChanged();
    partial void OnSpectPtsPerHourWaitingTextChanged(string value) => OnSpectInputChanged();
    partial void OnSpectPtsPerHourCameraTextChanged(string value) => OnSpectInputChanged();
    partial void OnSpectPtsPerHourStressTextChanged(string value) => OnSpectInputChanged();
    partial void OnSpectHourlyLimitTextChanged(string value) => OnSpectInputChanged();

    /// <summary>Strict numeric parse: NaN for anything that is not a number (blank included). Callers decide what blank means.</summary>
    private static double ParseStrict(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return double.NaN;
        var t = s.Trim();
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out var v)) return v;
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
        return double.NaN;
    }

    private string SpectMaxPtsText(string region) => region switch
    {
        SpectRegions.Waiting => SpectMaxPtsWaitingText,
        SpectRegions.Camera => SpectMaxPtsCameraText,
        SpectRegions.Stress => SpectMaxPtsStressText,
        _ => "1"
    };

    private string SpectPtsPerHourText(string region) => region switch
    {
        SpectRegions.Waiting => SpectPtsPerHourWaitingText,
        SpectRegions.Camera => SpectPtsPerHourCameraText,
        SpectRegions.Stress => SpectPtsPerHourStressText,
        _ => ""
    };

    /// <summary>Max patients at once; NaN if the text is not a valid number ≥ 1 (validated before solving).</summary>
    private double SpectMaxPts(string region)
    {
        double v = ParseStrict(SpectMaxPtsText(region));
        return v >= 1 ? v : double.NaN;
    }

    /// <summary>Different patients per hour; 0 when blank (not specified), NaN if invalid.</summary>
    private double SpectPtsPerHour(string region)
    {
        var t = SpectPtsPerHourText(region);
        if (string.IsNullOrWhiteSpace(t)) return 0;
        double v = ParseStrict(t);
        return v >= 0 ? v : double.NaN;
    }


    private void OnSpectInputChanged()
    {
        if (IsLoadingProject) return;
        MarkDirty();
        RecomputeAllSpect();
    }

    /// <summary>Studies change every barrier, so recompute all, then show the selected barrier's trace.</summary>
    public void RecomputeAllSpect()
    {
        if (!IsSPECT || !HasActiveDesign || IsLoadingProject) return;
        EnsureSpectMarkers();
        foreach (var b in Barriers) ComputeBarrier(b);
        ShowCachedTrace(SelectedBarrier);
    }

    /// <summary>Source regions a barrier sees: fixed by room type, or chosen per barrier in the suite.</summary>
    private List<string> SpectRegionsFor(BarrierItemViewModel b) => SelectedRoomCategory switch
    {
        SpectCameraRoom => new() { SpectRegions.Camera },
        SpectWaitingRoom => new() { SpectRegions.Waiting },
        SpectStressRoom => new() { SpectRegions.Stress },
        _ => new List<string>()
            .Concat(b.SpectSeesWaiting ? new[] { SpectRegions.Waiting } : Array.Empty<string>())
            .Concat(b.SpectSeesCamera ? new[] { SpectRegions.Camera } : Array.Empty<string>())
            .Concat(b.SpectSeesStress ? new[] { SpectRegions.Stress } : Array.Empty<string>())
            .ToList()
    };

    // ===== Per-region source positions (Cardiac SPECT Suite) =====
    // The orange source marker is the patient on the camera table. In the suite, waiting and stress patients
    // have their own markers so each region is calculated from where those patients actually are.
    [ObservableProperty] private double spectWaitingX;
    [ObservableProperty] private double spectWaitingY;
    [ObservableProperty] private double spectStressX;
    [ObservableProperty] private double spectStressY;
    /// <summary>False until the user drags the marker; until then it sits on the camera source (conservative).</summary>
    [ObservableProperty] private bool spectWaitingPlaced;
    [ObservableProperty] private bool spectStressPlaced;

    /// <summary>Put unplaced region markers on the camera source so they're visible and conservative.</summary>
    public void EnsureSpectMarkers()
    {
        if (!SpectWaitingPlaced) { SpectWaitingX = TubeX; SpectWaitingY = TubeY; }
        if (!SpectStressPlaced) { SpectStressX = TubeX; SpectStressY = TubeY; }
    }

    /// <summary>Called by the canvas while a region marker is dragged.</summary>
    public void MoveSpectMarker(string region, double x, double y)
    {
        if (region == SpectRegions.Waiting) { SpectWaitingX = x; SpectWaitingY = y; SpectWaitingPlaced = true; }
        else if (region == SpectRegions.Stress) { SpectStressX = x; SpectStressY = y; SpectStressPlaced = true; }
        MarkDirty();
    }

    /// <summary>
    /// Distance (m, including the barrier's usual offset beyond the wall) from a region's source to the barrier's
    /// calculation point. Floors/ceilings and single-room categories use the barrier's normal distance.
    /// </summary>
    private double SpectRegionDistance(BarrierItemViewModel b, string region)
    {
        double normal = Math.Max(1e-6, b.EffectiveDistance_m);
        if (!IsSpectSuite || !b.IsDrawable || region == SpectRegions.Camera) return normal;

        double x = region == SpectRegions.Waiting ? SpectWaitingX : region == SpectRegions.Stress ? SpectStressX : TubeX;
        double y = region == SpectRegions.Waiting ? SpectWaitingY : region == SpectRegions.Stress ? SpectStressY : TubeY;
        double raw = DistancePointToSegment(x, y, b.X1, b.Y1, b.X2, b.Y2) * CmPerPixel / 100.0;
        return Math.Max(1e-6, raw + b.ExtraOffset_m);
    }

    // ===== Compute =====
    /// <summary>
    /// Everything the solver needs, or a list of reasons it must not run. An invalid design produces an error,
    /// never a thickness (a silent 0 mm from a typo is the worst outcome a shielding tool can have).
    /// </summary>
    private NuclearBarrierRequest? BuildSpectRequest(BarrierItemViewModel b, out List<string> errors, out List<SpectStudyViewModel> studies, out List<string> regions)
    {
        errors = new List<string>();
        bool controlled = string.Equals(b.Zone, "Controlled", StringComparison.OrdinalIgnoreCase);
        regions = SpectRegionsFor(b);

        // Studies: an ENABLED study must be fully valid even if its count turns out to be 0.
        var enabled = SpectStudies.Where(s => s.IsEnabled).ToList();
        foreach (var s in enabled) errors.AddRange(s.InvalidInputs());
        studies = enabled.Where(s => s.IsCounted).ToList();
        if (enabled.Count == 0) errors.Add("No study is enabled (SPECT Parameters).");
        else if (studies.Count == 0 && errors.Count == 0) errors.Add("Enabled studies all have 0 patients/week.");

        double pHour = 0;
        if (!controlled)
        {
            pHour = ParseStrict(SpectHourlyLimitText);
            if (!(pHour >= 0)) errors.Add($"Hourly limit '{SpectHourlyLimitText}' is not a number ≥ 0 mSv.");
        }
        if (regions.Count == 0) errors.Add("This barrier sees no SPECT source region (tick Waiting / Camera / Stress).");

        var req = new NuclearBarrierRequest
        {
            BarrierId = b.Label,
            Occupancy = b.Occupancy,
            P_week_mSv = b.PMGyWeek,
            P_hour_mSv = pHour,
            Material = MapSpectMaterial(b.MaterialName) ?? "Lead",
        };
        foreach (var reg in regions)
        {
            double r = SpectRegionDistance(b, reg);
            double atOnce = SpectMaxPts(reg);
            if (double.IsNaN(atOnce)) errors.Add($"Max patients at once ({reg}) '{SpectMaxPtsText(reg)}' is not a number ≥ 1.");
            double perHour = SpectPtsPerHour(reg);
            if (double.IsNaN(perHour)) errors.Add($"Different patients in any hour ({reg}) '{SpectPtsPerHourText(reg)}' is not a number ≥ 0.");
            req.Regions.Add(new RegionExposure(reg, r, atOnce, perHour));
        }
        foreach (var pl in b.PreexistingLayers)
        {
            var m = MapSpectMaterial(pl.MaterialName);
            if (m == null) continue;
            if (double.IsNaN(pl.Thickness_mm) || pl.Thickness_mm < 0) errors.Add($"Pre-existing {pl.MaterialName}: thickness must be a number ≥ 0.");
            else if (pl.Thickness_mm > 0) req.PreExisting.Add(new ShieldLayer(m, pl.Thickness_mm));
        }
        if (errors.Count > 0) return null;

        foreach (var s in studies)
            req.Workloads.Add(new ProtocolWorkload(SpectProtocols.Build(s.ToStudy()), s.PatientsPerWeek));
        return req;
    }

    private void ComputeSpectBarrier(BarrierItemViewModel b)
    {
        var lib = NuclideLibrary.Default;
        bool controlled = string.Equals(b.Zone, "Controlled", StringComparison.OrdinalIgnoreCase);

        var req = BuildSpectRequest(b, out var errors, out var studies, out var regions);
        NuclearBarrierResult res;
        try
        {
            if (req == null) throw new ArgumentException("Invalid shielding input:\n• " + string.Join("\n• ", errors));
            res = NuclearBarrierSolver.Solve(req, lib);   // the engine re-validates; both paths end here
        }
        catch (Exception ex)
        {
            // No thickness, no gauge, no legend entry: the barrier shows as unsolved until the input is fixed.
            b.RequiredThickness_mm = double.NaN;
            b.GoverningLimit = "";
            ResultSummary = $"{BareBarrierTitle(b)}: NOT CALCULATED — {ex.Message.Replace("\n", " ")}";
            TraceItems.Add(new("Modality", "Nuclear medicine (SPECT), patient as source"));
            TraceItems.Add(new("⛔ Not calculated", "Fix the input below; no shielding value is reported for this barrier."));
            foreach (var line in ex.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
                TraceItems.Add(new("⛔ Input error", line.TrimStart('•', ' ')));
            if (ex is not ArgumentException) TraceItems.Add(new("Error", ex.Message));
            CacheTraceToBarrier(b);
            return;
        }

        // ---- Trace (same panel/report as other modalities) ----
        TraceItems.Add(new("Modality", "Nuclear medicine (SPECT), patient as source"));
        TraceItems.Add(new("Source regions seen", string.Join(", ", regions)));
        if (IsSpectSuite && b.IsDrawable)
        {
            foreach (var rx in req!.Regions)
                TraceItems.Add(new($"Distance from {rx.Region} source (m)", rx.Distance_m.ToString("G4", CultureInfo.CurrentCulture)));
            if (regions.Contains(SpectRegions.Waiting) && !SpectWaitingPlaced)
                TraceItems.Add(new("⚠ Warning", "Waiting source marker has not been placed; it sits on the camera source (conservative). Drag the blue W marker to where injected patients wait."));
            if (regions.Contains(SpectRegions.Stress) && !SpectStressPlaced)
                TraceItems.Add(new("⚠ Warning", "Stress source marker has not been placed; it sits on the camera source (conservative). Drag the green S marker to the stress/injection position."));
        }
        else
        {
            TraceItems.Add(new("Distance used r (m)", req!.Regions[0].Distance_m.ToString("G4", CultureInfo.CurrentCulture)));
        }
        foreach (var s in studies)
            TraceItems.Add(new($"Study: {s.Protocol}", s.Describe()));
        if (!controlled)
            foreach (var rx in req.Regions)
                TraceItems.Add(new($"Patients ({rx.Region})", $"{rx.MaxSimultaneousPatients:G4} at once; {(rx.PatientsPerHour > 0 ? rx.PatientsPerHour.ToString("G4", CultureInfo.CurrentCulture) : "n/a")} different per hour"));
        foreach (var kv in res.Trace)
            TraceItems.Add(kv);
        foreach (var c in res.Contributions)
            TraceItems.Add(new($"{c.NuclideId} shielded", $"{c.Shielded_week_mSv:G4} mSv/wk, {c.Shielded_hour_mSv:G4} mSv in peak hour"));
        foreach (var w in res.Warnings)
            TraceItems.Add(new("⚠ Warning", w));
        TraceItems.Add(new("Data sources", "Kusano & Caldwell, Health Phys 107:60 (2014); Oumano et al., JACMP (2025) for Tc-99m in gypsum, LW concrete, steel, glass"));

        // ---- Result (same rounding/display path as PET) ----
        b.RequiredThickness_mm = res.RequiredThickness_mm;
        b.GoverningLimit = res.Governing == "None" ? "" : res.Governing;
        b.ApplyThicknessRounding();

        var req_ = !string.IsNullOrWhiteSpace(b.ThicknessShortDisplay) ? b.ThicknessShortDisplay : $"{b.RequiredThickness_mm:F2} mm";
        var fin = !string.IsNullOrWhiteSpace(b.FinalThicknessShortDisplay) ? b.FinalThicknessShortDisplay : req_;
        string gov = res.Governing == "None" ? "" : $" [{res.Governing.ToLowerInvariant()} limit]";

        ResultSummary = b.HasThicknessRounding
            ? $"{BareBarrierTitle(b)}: {b.MaterialName} → Req: {req_} -> Final: {fin}{gov}"
            : $"{BareBarrierTitle(b)}: {b.MaterialName} → {req_}{gov}";

        CacheTraceToBarrier(b);
    }

    // ===== Save / load =====
    internal List<SpectStudyDto> SpectStudiesToDto() => SpectStudies.Select(s => new SpectStudyDto
    {
        Protocol = s.Protocol,
        IsEnabled = s.IsEnabled,
        PatientsPerWeekText = s.PatientsPerWeekText,
        Activity1Text = s.Activity1Text,
        Activity2Text = s.Activity2Text,
        WaitText = s.WaitText,
        ScanText = s.ScanText,
        GapText = s.GapText,
        StressText = s.StressText,
        PostStressWaitText = s.PostStressWaitText,
    }).ToList();

    internal void SpectStudiesFromDto(List<SpectStudyDto>? dtos)
    {
        // Old project files have no SPECT data: keep defaults.
        if (dtos == null || dtos.Count == 0) return;
        foreach (var d in dtos)
        {
            var s = SpectStudies.FirstOrDefault(x => x.Protocol == d.Protocol);
            if (s == null) continue;
            s.IsEnabled = d.IsEnabled;
            s.PatientsPerWeekText = d.PatientsPerWeekText ?? "0";
            s.Activity1Text = d.Activity1Text ?? "";
            s.Activity2Text = d.Activity2Text ?? "";
            s.WaitText = d.WaitText ?? "";
            s.ScanText = d.ScanText ?? "";
            s.GapText = d.GapText ?? "";
            s.StressText = d.StressText ?? "";
            s.PostStressWaitText = d.PostStressWaitText ?? "";
        }
    }

    /// <summary>Bullets for the exported report's assumptions section.</summary>
    public IEnumerable<string> SpectReportAssumptions()
    {
        if (!IsSPECT) yield break;
        foreach (var s in SpectStudies.Where(x => x.IsCounted))
            yield return $"{s.Protocol}: {s.Describe()}.";
        if (IsSpectSuite)
            yield return "Camera, waiting and stress patients are modeled at their own positions as marked on the layout; each barrier's distance is measured from each source position.";
        yield return $"Max patients present at once: waiting {SpectMaxPtsWaitingText}, camera {SpectMaxPtsCameraText}, stress {SpectMaxPtsStressText}.";
        string PerHour(string t) => string.IsNullOrWhiteSpace(t) ? "not specified" : t;
        yield return $"Different patients passing through in any one hour (turnover): waiting {PerHour(SpectPtsPerHourWaitingText)}, camera {PerHour(SpectPtsPerHourCameraText)}, stress {PerHour(SpectPtsPerHourStressText)}. " +
                     "The hourly dose uses the larger of at-once and per-hour counts times the worst single-patient hour in that room (no decay credit between patients).";
        yield return $"Uncontrolled areas checked against {SpectHourlyLimitText} mSv in any one hour in addition to the weekly design goal. " +
                     "This hourly figure is the 10 CFR 20.1301(a)(2) public limit; that rule excludes dose from patients released under 10 CFR 35.75, so for a released-patient pathway it is applied here as a conservative design check, not as the governing regulation.";
        if (IsSpectSuite)
            yield return "In the suite layout each barrier is evaluated at its own distance from each source region (camera, waiting W marker, stress S marker on the drawing).";
        yield return "Dose-rate constants are for a point source in air (no patient self-attenuation), which is conservative.";
        yield return "Pre-existing layers are credited by multiplying each layer's broad-beam transmission; for dissimilar layers this is an approximation, not a multilayer transport calculation.";
        yield return "Barrier transmission is for a broad beam through the barrier only; room scatter is not modelled and can matter when the required transmission is very low (≲1%), per Oumano et al. 2025.";
        yield return "Sources other than patients (hot lab, generator, dose preparation, waste storage) are not included and need a separate evaluation where they can be seen from a barrier.";
        yield return "Transmission data: Kusano & Caldwell, Health Phys 107:60 (2014); Oumano et al., JACMP (2025) for Tc-99m in gypsum, lightweight concrete, steel and glass.";
    }
}
