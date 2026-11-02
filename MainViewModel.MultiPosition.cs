using CommunityToolkit.Mvvm.ComponentModel;
using RadiationShieldingDesign.Engine.Xray;
using System.Collections.ObjectModel;
using System.Globalization;

namespace RadiationShieldingDesign;

/// <summary>
/// Multi-position x-ray mode (opt-in) for rad rooms and R&amp;F rooms.
///
/// Instead of one tube location and one workload distribution per barrier, every tube position contributes
/// to every barrier with its own NCRP 147 workload distribution, distance and role (primary/secondary):
///   Rad room: Table ("Rad Room (floor or other barriers)") + Chest stand ("Rad Room (chest bucky)"), same patients.
///   R&amp;F:     Fluoro at table ("Fluoro Tube (R&amp;F room)", secondary only) + overhead tube at table
///            ("Rad Tube (R&amp;F room)") + overhead tube at chest stand ("Rad Room (chest bucky)").
/// All components are summed and the thickness is solved numerically (<see cref="MultiPositionSolver"/>).
/// The table/overhead and fluoro positions use the existing source marker; the chest stand has its own marker.
/// </summary>
public partial class MainViewModel
{
    public const string MpTable = "Table", MpChest = "Chest", MpFluoro = "Fluoro";

    public IReadOnlyList<string> MpRoleOptions { get; } = new[] { "Auto", "Primary", "Secondary", "Off" };

    public bool IsRfRoom => SelectedRoomCategory is "Rad Tube (R&F room)" or "Fluoro Tube (R&F room)";
    public bool IsMultiPositionAvailable => SelectedRoomCategory is "Radiography" || IsRfRoom;
    public bool IsMultiPositionActive => MultiPositionEnabled && IsMultiPositionAvailable;

    /// <summary>The single-K air-kerma card doesn't apply in SPECT or multi-position mode.</summary>
    public bool ShowAirKermaCard => !IsSPECT && !IsMultiPositionActive;

    public string MpTableLabel => IsRfRoom ? "Overhead tube → table" : "Table";
    public string MpChestLabel => IsRfRoom ? "Overhead tube → chest stand" : "Chest stand";
    public string MpFluoroLabel => "Fluoro (under table)";

    [ObservableProperty] private bool multiPositionEnabled;
    [ObservableProperty] private string mpTablePatientsText = "";
    [ObservableProperty] private string mpChestPatientsText = "";
    [ObservableProperty] private string mpFluoroPatientsText = "";
    /// <summary>Workload per patient (mA·min); blank = NCRP 147 W_norm for that distribution.</summary>
    [ObservableProperty] private string mpTableWText = "";
    [ObservableProperty] private string mpChestWText = "";
    [ObservableProperty] private string mpFluoroWText = "";
    /// <summary>Chest focal-spot-to-receptor distance (m); added to the chest-stand marker distance for chest primary.</summary>
    [ObservableProperty] private string mpChestSidText = "1.83";

    // Chest stand marker (patient / image receptor at the chest stand)
    [ObservableProperty] private double chestX;
    [ObservableProperty] private double chestY;
    [ObservableProperty] private bool chestPlaced;

    private void RaiseMultiPositionState()
    {
        OnPropertyChanged(nameof(IsRfRoom));
        OnPropertyChanged(nameof(IsMultiPositionAvailable));
        OnPropertyChanged(nameof(IsMultiPositionActive));
        OnPropertyChanged(nameof(ShowAirKermaCard));
        OnPropertyChanged(nameof(MpTableLabel));
        OnPropertyChanged(nameof(MpChestLabel));
    }

    partial void OnMultiPositionEnabledChanged(bool value)
    {
        RaiseMultiPositionState();
        if (IsLoadingProject) return;
        if (value) ApplyMultiPositionDefaults();
        EnsureChestMarker();
        MarkDirty();
        RecomputeAllAndShow();
    }

    partial void OnMpTablePatientsTextChanged(string value) => OnMultiPositionInputChanged();
    partial void OnMpChestPatientsTextChanged(string value) => OnMultiPositionInputChanged();
    partial void OnMpFluoroPatientsTextChanged(string value) => OnMultiPositionInputChanged();
    partial void OnMpTableWTextChanged(string value) => OnMultiPositionInputChanged();
    partial void OnMpChestWTextChanged(string value) => OnMultiPositionInputChanged();
    partial void OnMpFluoroWTextChanged(string value) => OnMultiPositionInputChanged();
    partial void OnMpChestSidTextChanged(string value) => OnMultiPositionInputChanged();

    private void OnMultiPositionInputChanged()
    {
        if (IsLoadingProject || !IsMultiPositionActive) return;
        MarkDirty();
        RecomputeAllAndShow();
    }

    /// <summary>Fill empty patient counts: rad room uses the room's patients/week for both positions (NCRP splits
    /// each patient's workload between chest and table); R&amp;F uses NCRP 147 typical averages.</summary>
    private void ApplyMultiPositionDefaults()
    {
        string N(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);
        double NcrpN(string scenario, double fallback) =>
            _data.TryGetPrimaryPatients(scenario, out var avg, out _) && avg > 0 ? avg : fallback;

        if (IsRfRoom)
        {
            if (string.IsNullOrWhiteSpace(MpFluoroPatientsText)) MpFluoroPatientsText = N(NcrpN("Fluoro Tube (R&F room)", 20));
            if (string.IsNullOrWhiteSpace(MpTablePatientsText)) MpTablePatientsText = N(NcrpN("Rad Tube (R&F room)", 25));
            if (string.IsNullOrWhiteSpace(MpChestPatientsText)) MpChestPatientsText = N(NcrpN("Rad Tube (R&F room)", 25));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(MpTablePatientsText)) MpTablePatientsText = N(PatientsPerWeek);
            if (string.IsNullOrWhiteSpace(MpChestPatientsText)) MpChestPatientsText = N(PatientsPerWeek);
        }
    }

    public void RecomputeAllAndShow()
    {
        if (!HasActiveDesign || IsLoadingProject) return;
        foreach (var b in Barriers) ComputeBarrier(b);
        ShowCachedTrace(SelectedBarrier);
    }

    // ===== Chest marker =====
    /// <summary>Until the user drags it, put the chest marker just in front of the chest bucky wall (or beside the source).</summary>
    public void EnsureChestMarker()
    {
        if (ChestPlaced) return;
        var wall = Barriers.FirstOrDefault(x => x.Designation == BarrierDesignation.ChestBuckyWall && x.IsDrawable);
        if (wall != null)
        {
            double mx = (wall.X1 + wall.X2) / 2, my = (wall.Y1 + wall.Y2) / 2;
            double dx = TubeX - mx, dy = TubeY - my, len = Math.Sqrt(dx * dx + dy * dy);
            double step = 30.0 / Math.Max(1e-6, CmPerPixel);   // 30 cm into the room
            if (len > 1e-6) { ChestX = mx + dx / len * step; ChestY = my + dy / len * step; }
            else { ChestX = mx; ChestY = my; }
        }
        else
        {
            ChestX = TubeX + 60; ChestY = TubeY;
        }
    }

    public void MoveChestMarker(double x, double y)
    {
        ChestX = x; ChestY = y; ChestPlaced = true;
        MarkDirty();
    }

    // ===== Positions and roles =====
    private sealed record TubePosition(string Key, string Name, string Scenario, double Patients, double WUser);

    private List<TubePosition> ActivePositions()
    {
        var list = new List<TubePosition>();
        if (IsRfRoom)
            list.Add(new(MpFluoro, MpFluoroLabel, "Fluoro Tube (R&F room)", ParseOrZero(MpFluoroPatientsText), ParseOrZero(MpFluoroWText)));
        list.Add(new(MpTable, MpTableLabel, IsRfRoom ? "Rad Tube (R&F room)" : "Rad Room (floor or other barriers)",
            ParseOrZero(MpTablePatientsText), ParseOrZero(MpTableWText)));
        list.Add(new(MpChest, MpChestLabel, "Rad Room (chest bucky)", ParseOrZero(MpChestPatientsText), ParseOrZero(MpChestWText)));
        return list;
    }

    private static string ExplicitRole(BarrierItemViewModel b, string key) => key switch
    {
        MpTable => b.MpTableRole,
        MpChest => b.MpChestRole,
        _ => b.MpFluoroRole
    } ?? "Auto";

    /// <summary>Auto: chest bucky wall is primary from the chest stand and secondary from the table; other barriers
    /// marked Primary are primary from the table; everything else is secondary. Fluoro is always secondary.</summary>
    private static string AutoRole(BarrierItemViewModel b, string key) => key switch
    {
        MpFluoro => "Secondary",
        MpChest => b.Designation == BarrierDesignation.ChestBuckyWall && b.Kind == BarrierKind.Primary ? "Primary" : "Secondary",
        _ => b.Designation == BarrierDesignation.ChestBuckyWall ? "Secondary"
             : b.Kind == BarrierKind.Primary ? "Primary" : "Secondary"
    };

    private static string ResolveRole(BarrierItemViewModel b, string key)
    {
        var r = ExplicitRole(b, key);
        return string.IsNullOrWhiteSpace(r) || r == "Auto" ? AutoRole(b, key) : r;
    }

    /// <summary>Source → occupied point (m). Table and fluoro use the barrier's normal distance (source marker, overrides,
    /// floor/ceiling rules). The chest stand measures from its own marker; chest primary adds the SID.</summary>
    private double PositionDistance(BarrierItemViewModel b, string key, bool primary)
    {
        double normal = Math.Max(1e-6, b.EffectiveDistance_m);
        if (key != MpChest || !b.IsDrawable) return normal;

        double raw = DistancePointToSegment(ChestX, ChestY, b.X1, b.Y1, b.X2, b.Y2) * CmPerPixel / 100.0;
        double d = raw + b.ExtraOffset_m;
        if (primary) d += Math.Max(0, ParseOrZero(MpChestSidText));
        return Math.Max(1e-6, d);
    }

    /// <summary>Transmission of existing construction for one distribution's spectrum (same conventions as the
    /// single-source path: coefficient material mapping and lightweight-concrete density scaling).</summary>
    private double PreStackFor(BarrierItemViewModel b, string scenario, bool primary)
    {
        double B = 1.0;
        foreach (var layer in b.PreexistingLayers)
        {
            var name = layer?.MaterialName ?? "";
            if (string.IsNullOrWhiteSpace(name) || name.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
            double t = layer!.Thickness_mm;
            if (!(t > 0)) continue;

            (double a, double b, double g) c = default;
            bool ok = false;
            foreach (var key in new[] { CoeffMaterialName(name), name })
            {
                ok = primary ? _data.TryGetPrimaryCoeff(scenario, key, out c) : _data.TryGetSecondaryCoeff(scenario, key, out c);
                if (ok && c.a > 0 && c.g > 0) break;
                ok = false;
            }
            if (!ok) continue;

            var rho = _data.Materials.TryGetValue(name, out var m) ? m.Density_g_cm3 : 0;
            B *= MultiPositionSolver.Transmission(c.a, c.b, c.g, t * DensityScale(name, rho));
        }
        return B;
    }

    // ===== Compute =====
    private void ComputeMultiPositionBarrier(BarrierItemViewModel b)
    {
        var matName = string.IsNullOrWhiteSpace(b.MaterialName) ? "Lead" : b.MaterialName;
        double preShield = b.Kind == BarrierKind.Primary && b.ApplyPreShield ? b.PreShield_mm : 0.0;
        var req = new MultiPositionRequest { BarrierId = b.Label, Occupancy = b.Occupancy, P_mGy_week = b.PMGyWeek };

        TraceItems.Add(new("Mode", IsRfRoom ? "Multi-position R&F (fluoro + overhead tube at table + chest stand)"
                                            : "Multi-position rad room (table + chest stand)"));
        if (!ChestPlaced)
            TraceItems.Add(new("⚠ Warning", "Chest stand marker was placed automatically in front of the chest bucky wall. Drag the purple C marker to the chest stand."));

        foreach (var p in ActivePositions())
        {
            string role = ResolveRole(b, p.Key);
            bool auto = ExplicitRole(b, p.Key) is null or "" or "Auto";
            TraceItems.Add(new($"{p.Name}", $"{role}{(auto ? " (auto)" : "")}; {p.Scenario}; N={p.Patients:G4}/wk" +
                (p.WUser > 0 ? $"; W={p.WUser:G4} mA·min/pt" : "")));
            if (role == "Off" || !(p.Patients > 0)) continue;

            // Secondary (leakage + scatter) always applies when the position is on. When this position is aimed at the
            // barrier, its scatter toward the wall also passes through the image receptor, so it gets the same pre-shield.
            bool aimed = role == "Primary";
            AddComponent(p, primary: false, receptor: aimed ? preShield : 0.0);
            if (aimed) AddComponent(p, primary: true, receptor: preShield);
        }

        void AddComponent(TubePosition p, bool primary, double receptor)
        {
            double k;
            bool okK = primary ? _data.TryGetPrimaryK1(p.Scenario, out k) : _data.TryGetSecondaryK1(p.Scenario, out k);
            if (!okK || !(k > 0))
            {
                if (primary) TraceItems.Add(new($"{p.Name} – primary", $"No primary K for {p.Scenario} (beam intercepted by the image receptor); skipped."));
                return;
            }
            if (p.WUser > 0)
            {
                double wn;
                bool okW = primary ? _data.TryGetPrimaryWnorm(p.Scenario, out wn) : _data.TryGetSecondaryWnorm(p.Scenario, out wn);
                if (okW && wn > 0) k *= p.WUser / wn;
            }
            (double a, double b, double g) c;
            bool okC = primary ? _data.TryGetPrimaryCoeff(p.Scenario, matName, out c) : _data.TryGetSecondaryCoeff(p.Scenario, matName, out c);
            if (!okC)
            {
                TraceItems.Add(new("⚠ Warning", $"No {(primary ? "primary" : "secondary")} coefficients for {matName} ({p.Scenario}); component skipped."));
                return;
            }
            req.Components.Add(new XrayComponent(
                $"{p.Name} – {(primary ? "primary" : "secondary")}",
                primary, k, p.Patients, primary ? b.UseFactor : 1.0,
                PositionDistance(b, p.Key, primary), c.a, c.b, c.g,
                receptor,
                PreStackFor(b, p.Scenario, primary)));
        }

        var res = MultiPositionSolver.Solve(req);
        foreach (var kv in res.Trace) TraceItems.Add(kv);

        b.RequiredThickness_mm = res.RequiredThickness_mm;
        b.ApplyThicknessRounding();

        var reqDisp = !string.IsNullOrWhiteSpace(b.ThicknessShortDisplay) ? b.ThicknessShortDisplay : $"{b.RequiredThickness_mm:F2} mm";
        var finDisp = !string.IsNullOrWhiteSpace(b.FinalThicknessShortDisplay) ? b.FinalThicknessShortDisplay : reqDisp;
        var top = res.Components.FirstOrDefault();
        string driver = top != null && b.RequiredThickness_mm > 0 ? $" [{top.Label} {top.Fraction:P0}]" : "";
        ResultSummary = b.HasThicknessRounding
            ? $"{BareBarrierTitle(b)}: {b.MaterialName} → Req: {reqDisp} -> Final: {finDisp}{driver}"
            : $"{BareBarrierTitle(b)}: {b.MaterialName} → {reqDisp}{driver}";

        CacheTraceToBarrier(b);
    }

    public IEnumerable<string> MultiPositionReportAssumptions()
    {
        if (!IsMultiPositionActive) yield break;
        yield return IsRfRoom
            ? "Tube positions modeled separately (NCRP 147 workload distributions): fluoroscopy at the table, overhead tube at the table, and overhead tube at the chest stand; contributions are summed at each barrier."
            : "Tube positions modeled separately (NCRP 147 workload distributions): table (floor or other barriers) and chest stand (chest bucky); contributions are summed at each barrier.";
        foreach (var p in ActivePositions())
            yield return $"{p.Name}: {p.Patients:G4} patients/week ({p.Scenario}" + (p.WUser > 0 ? $", {p.WUser:G4} mA·min/patient)" : ", NCRP workload per patient)");
        yield return $"Chest-stand primary distance includes a {ParseOrZero(MpChestSidText):G3} m focal-spot-to-receptor distance.";
        yield return "Each barrier includes primary radiation from positions aimed at it and secondary radiation from every position.";
    }
}
