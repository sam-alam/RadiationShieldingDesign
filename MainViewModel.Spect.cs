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

    /// <summary>Public dose limit in any one hour (mSv), applied to uncontrolled barriers.</summary>
    [ObservableProperty] private string spectHourlyLimitText = "0.02";

    partial void OnSpectMaxPtsWaitingTextChanged(string value) => OnSpectInputChanged();
    partial void OnSpectMaxPtsCameraTextChanged(string value) => OnSpectInputChanged();
    partial void OnSpectMaxPtsStressTextChanged(string value) => OnSpectInputChanged();
    partial void OnSpectHourlyLimitTextChanged(string value) => OnSpectInputChanged();

    private double SpectMaxPts(string region) => Math.Max(1.0, region switch
    {
        SpectRegions.Waiting => ParseOrZero(SpectMaxPtsWaitingText),
        SpectRegions.Camera => ParseOrZero(SpectMaxPtsCameraText),
        SpectRegions.Stress => ParseOrZero(SpectMaxPtsStressText),
        _ => 1.0
    });

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
    private void ComputeSpectBarrier(BarrierItemViewModel b)
    {
        var lib = NuclideLibrary.Default;
        double r = Math.Max(1e-6, b.EffectiveDistance_m);
        bool controlled = string.Equals(b.Zone, "Controlled", StringComparison.OrdinalIgnoreCase);
        double pHour = controlled ? 0.0 : ParseOrZero(SpectHourlyLimitText);
        var regions = SpectRegionsFor(b);
        var studies = SpectStudies.Where(s => s.IsEnabled && s.PatientsPerWeek > 0).ToList();

        var req = new NuclearBarrierRequest
        {
            BarrierId = b.Label,
            Occupancy = b.Occupancy,
            P_week_mSv = b.PMGyWeek,
            P_hour_mSv = pHour,
            Material = MapSpectMaterial(b.MaterialName) ?? "Lead",
        };
        foreach (var s in studies)
            req.Workloads.Add(new ProtocolWorkload(SpectProtocols.Build(s.ToStudy()), s.PatientsPerWeek));
        var regionDist = regions.ToDictionary(reg => reg, reg => SpectRegionDistance(b, reg));
        foreach (var reg in regions)
            req.Regions.Add(new RegionExposure(reg, regionDist[reg], SpectMaxPts(reg)));
        foreach (var pl in b.PreexistingLayers)
        {
            var m = MapSpectMaterial(pl.MaterialName);
            if (m != null && pl.Thickness_mm > 0) req.PreExisting.Add(new ShieldLayer(m, pl.Thickness_mm));
        }

        NuclearBarrierResult res;
        try
        {
            res = NuclearBarrierSolver.Solve(req, lib);
        }
        catch (Exception ex)
        {
            b.RequiredThickness_mm = double.NaN;
            ResultSummary = $"{BareBarrierTitle(b)}: SPECT calculation error: {ex.Message}";
            TraceItems.Add(new("Error", ex.Message));
            CacheTraceToBarrier(b);
            return;
        }

        // ---- Trace (same panel/report as other modalities) ----
        TraceItems.Add(new("Modality", "Nuclear medicine (SPECT), patient as source"));
        TraceItems.Add(new("Source regions seen", regions.Count > 0 ? string.Join(", ", regions) : "none"));
        if (IsSpectSuite && b.IsDrawable)
        {
            foreach (var reg in regions)
                TraceItems.Add(new($"Distance from {reg} source (m)", regionDist[reg].ToString("G4", CultureInfo.CurrentCulture)));
            if (regions.Contains(SpectRegions.Waiting) && !SpectWaitingPlaced)
                TraceItems.Add(new("⚠ Warning", "Waiting source marker has not been placed; it sits on the camera source (conservative). Drag the blue W marker to where injected patients wait."));
            if (regions.Contains(SpectRegions.Stress) && !SpectStressPlaced)
                TraceItems.Add(new("⚠ Warning", "Stress source marker has not been placed; it sits on the camera source (conservative). Drag the green S marker to the stress/injection position."));
        }
        else
        {
            TraceItems.Add(new("Distance used r (m)", r.ToString("G4", CultureInfo.CurrentCulture)));
        }
        if (studies.Count == 0)
            TraceItems.Add(new("Studies", "none enabled (enter patients/week in SPECT Parameters)"));
        foreach (var s in studies)
            TraceItems.Add(new($"Study: {s.Protocol}", s.Describe()));
        if (!controlled)
            foreach (var reg in regions)
                TraceItems.Add(new($"Max patients at once ({reg})", SpectMaxPts(reg).ToString("G4", CultureInfo.CurrentCulture)));
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
        foreach (var s in SpectStudies.Where(x => x.IsEnabled && x.PatientsPerWeek > 0))
            yield return $"{s.Protocol}: {s.Describe()}.";
        if (IsSpectSuite)
            yield return "Camera, waiting and stress patients are modeled at their own positions as marked on the layout; each barrier's distance is measured from each source position.";
        yield return $"Max patients present at once: waiting {SpectMaxPtsWaitingText}, camera {SpectMaxPtsCameraText}, stress {SpectMaxPtsStressText}.";
        yield return $"Uncontrolled areas checked against {SpectHourlyLimitText} mSv in any one hour in addition to the weekly design goal.";
        yield return "Dose-rate constants are for a point source in air (no patient self-attenuation), which is conservative.";
        yield return "Transmission data: Kusano & Caldwell, Health Phys 107:60 (2014); Oumano et al., JACMP (2025) for Tc-99m in gypsum, lightweight concrete, steel and glass.";
    }
}
