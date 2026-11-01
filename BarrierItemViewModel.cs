using CommunityToolkit.Mvvm.ComponentModel;
using RadiationShieldingDesign.Engine;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
//using static RadiationShieldingDesign.MainViewModel;

namespace RadiationShieldingDesign;

public enum BarrierKind { Primary, Secondary }

public enum BarrierDesignation
{
    ChestBuckyWall,
    CrossTableLateralWall,
    Wall,
    Window,
    Door,
    Floor,
    Ceiling,
}

public enum ThicknessRoundingRule
{
    Auto = 0,                 // no rounding at all (exact solver thickness)
    RoundUpPlus1Step = 1,      // (first commercial >= required) + 1 step thicker
    RoundUpPlus2Steps = 2,     // +2 steps thicker
    Minimum_1_16 = 3,
    Minimum_1_32 = 4
}

public partial class BarrierItemViewModel : ObservableObject, IDisposable
{
    public Guid Id { get; }
    private readonly MainViewModel _owner;

    public BarrierItemViewModel(MainViewModel owner, Guid? id = null)
    {
        _owner = owner;
        Id = id ?? Guid.NewGuid();
        WeakEventManager<INotifyPropertyChanged, PropertyChangedEventArgs>
   .AddHandler(_owner, nameof(_owner.PropertyChanged), Owner_PropertyChanged);

    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        WeakEventManager<INotifyPropertyChanged, PropertyChangedEventArgs>
            .RemoveHandler(_owner, nameof(_owner.PropertyChanged), Owner_PropertyChanged);

        GC.SuppressFinalize(this);
    }


    private void Owner_PropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.LevelBelowHeight_ft))
        {
            OnPropertyChanged(nameof(ExtraOffset_m));
            OnPropertyChanged(nameof(EffectiveDistance_m));
        }
        if (e.PropertyName == nameof(MainViewModel.SelectedRoomCategory) ||
    e.PropertyName == nameof(MainViewModel.IsPET) ||
    e.PropertyName == nameof(MainViewModel.IsSPECT))
        {
            OnPropertyChanged(nameof(ShowPetRegionSelector));
            OnPropertyChanged(nameof(ShowSpectRegionSelector));
        }
    }

   // private bool _k1ManualOverride;
    [ObservableProperty] private bool _k1ManualOverride;


    [ObservableProperty] private string label = "";
    [ObservableProperty] private string name = "Barrier";
    [ObservableProperty] private BarrierKind kind = BarrierKind.Primary;
    [ObservableProperty] private BarrierDesignation designation;
    [ObservableProperty] private bool isSelected;
    // Linked-barrier helpers (UI toggles)
    [ObservableProperty] private bool hasLinkedSecondary = false; // Primary -> Secondary clone
    [ObservableProperty] private Guid? linkedSecondaryId = null;

    [ObservableProperty] private bool hasTwinSecondary = false;   // Secondary -> another Secondary clone
    [ObservableProperty] private Guid? twinSecondaryId = null;

    // For floor/ceiling auto-label behavior
    private bool _autoLabelFromDesignation;
    private string _lastAutoLabel = "";

    [ObservableProperty] private bool pOverridden = false;
    // Drawing geometry (canvas coordinates in pixels)
    [ObservableProperty] private double x1 = 350;
    [ObservableProperty] private double y1 = 200;
    [ObservableProperty] private double x2 = 750;
    [ObservableProperty] private double y2 = 200;

  
    // Material & calc inputs (default reasonable)
    [ObservableProperty] private string materialName = "Lead";
    [ObservableProperty] private double useFactor = 1.0;     // U
    [ObservableProperty] private double occupancy = 1.0;     // T
    [ObservableProperty] private double pMGyWeek = 0.02;    // P
    [ObservableProperty] private double k1 = 0.0;            // K1 (auto-filled from tables when possible)
    [ObservableProperty] private string k1Text = "0.0";
    private bool _k1Guard;
    [ObservableProperty] private bool useNcrpK = true;

    [ObservableProperty] private double n = 1.0;             // patients/8h day?
    [ObservableProperty] private bool applyPreShield = false;
    [ObservableProperty] private string preShieldType = "";  // e.g. "Table/Wall bucky receptor"
    [ObservableProperty] private double preShield_mm = 0.0;  // x_pre (computed from table by material & type)

    [ObservableProperty] private double requiredThickness_mm = 0.0;
    [ObservableProperty] private string traceText = "";

    [ObservableProperty] private string? protectedArea;

    [ObservableProperty] private Brush legendBrush = Brushes.Orange;
    [ObservableProperty] private string legendText = "";              // e.g., "Lead 1/16\", 1.6 mm"

    // Distances (m)
    [ObservableProperty] private double displayDistance_m = 0.0;  // raw measured (no offsets)
    [ObservableProperty] private double distanceOverride_m = 0.0; // user-entered
    [ObservableProperty] private double autoDistance_m = 2.0;
    
    private bool _suppressPOverride;
    [ObservableProperty] private string zone = "Uncontrolled";
    [ObservableProperty] private string kSource = ""; // e.g. "NCRP", "DAP×map", "Rate×Time", "Manual"
    [ObservableProperty] private bool isCtTertiary = false;        // CT-only toggle
    [ObservableProperty] private double tertiaryHeight_m = 1.0;
    // text proxy for the TextBox (lets the user type decimals freely)
    [ObservableProperty] private string tertiaryHeightText = "1.0";
    [ObservableProperty] private bool snapToSource = false;
    [ObservableProperty] private string distanceOverrideFtText = "";
    [ObservableProperty] private bool isHiddenOnCanvas = false;
    // Distance from isocenter to the scatter measurement point used for kerma map (meters)
    [ObservableProperty] private double scatterIsoToMeasurement_m;
    [ObservableProperty] private bool isLabelHidden = false;

    // Per-barrier kerma map values (all at 1 m, user-entered)
    [ObservableProperty] private double scatterRate_mGyPerHourAt1m;      // mGy/h @ 1 m
    [ObservableProperty] private double scatterUGyPerMasAt1m;           // µGy/mAs @ 1 m
    [ObservableProperty] private double scatterUGyPerGyCm2At1m;         // µGy/(Gy·cm²) @ 1 m

    [ObservableProperty] private PetSourceRegion petRegion = PetSourceRegion.Both;
    public bool CanUseNcrpK => !_owner.IsNuclear && !_owner.IsCT;

    // SPECT suite: which source regions this barrier sees (all at the drawn source-to-barrier distance)
    [ObservableProperty] private bool spectSeesWaiting = true;
    [ObservableProperty] private bool spectSeesCamera = true;
    [ObservableProperty] private bool spectSeesStress = true;

  

    private ThicknessRoundingRule _thicknessRoundingRule = ThicknessRoundingRule.Auto;
    public ThicknessRoundingRule ThicknessRoundingRule
    {
        get => _thicknessRoundingRule;
        set
        {
            if (_thicknessRoundingRule == value) return;
            _thicknessRoundingRule = value;
            ApplyThicknessRounding(); // updates Final*
            OnPropertyChanged(nameof(ThicknessRoundingRule));
        }
    }

    private bool _hasThicknessRounding;
    public bool HasThicknessRounding
    {
        get => _hasThicknessRounding;
        private set
        {
            if (_hasThicknessRounding == value) return;
            _hasThicknessRounding = value;
            OnPropertyChanged(nameof(HasThicknessRounding));
        }
    }

    // The value you will SAVE/EXPORT as "final"
    private double _finalThickness_mm;
    public double FinalThickness_mm
    {
        get => _finalThickness_mm;
        private set
        {
            if (Math.Abs(_finalThickness_mm - value) < 1e-9) return;
            _finalThickness_mm = value;
            OnPropertyChanged(nameof(FinalThickness_mm));
        }
    }

    private string _finalThicknessShortDisplay = "";
    public string FinalThicknessShortDisplay
    {
        get => _finalThicknessShortDisplay;
        private set
        {
            if (_finalThicknessShortDisplay == value) return;
            _finalThicknessShortDisplay = value;
            OnPropertyChanged(nameof(FinalThicknessShortDisplay));
        }
    }

    // Lead stepping list (what user steps through)
    // Use the exact same commercial gauge ladder as GaugeUtils
    private static readonly List<(double Mm, string Label)> LeadStepTable =
        GaugeUtils.GaugeTable.ToList();

    private static readonly List<double> LeadStepMm =
        LeadStepTable.Select(t => t.Mm).ToList();

    public void ApplyThicknessRounding()
    {
        // defaults
        HasThicknessRounding = false;
        FinalThickness_mm = RequiredThickness_mm;
        FinalThicknessShortDisplay = "";

        if (double.IsPositiveInfinity(RequiredThickness_mm))
        {
            HasThicknessRounding = false;
            FinalThickness_mm = RequiredThickness_mm;
            FinalThicknessShortDisplay = "Not solvable";
            return;
        }

        if (!double.IsFinite(RequiredThickness_mm) || RequiredThickness_mm <= 0)
            return;

        // Only lead uses gauge stepping + fractions in your UX
        if (!IsLead)
            return;

        double req = RequiredThickness_mm;

        // ----------------------------
        // AUTO baseline (old behavior)
        // ----------------------------
        var autoGauge = GaugeUtils.RoundUpGauge(req);   // next commercial >= req
        double autoMm = autoGauge.Mm;
        string autoLabel = autoGauge.Label;

        // We'll start from Auto and then apply step/floor rules on top
        double finalMm = autoMm;
        string finalLabel = autoLabel;

        // ----------------------------
        // Floors (as floors, NOT force)
        // ----------------------------
        // Look up floor gauges by label to avoid fragile hard-coded indices.
        // GaugeTable order: [0]=1/64", [1]=1/32", [2]=5/128", [3]=3/64", [4]=1/16", ...
        double floorMm = 0.0;
        string floorLabel = "";

        if (ThicknessRoundingRule == ThicknessRoundingRule.Minimum_1_16)
        {
            var gauge = LeadStepTable.First(g => g.Label == "1/16\"");
            floorMm = gauge.Mm;
            floorLabel = gauge.Label;
        }
        else if (ThicknessRoundingRule == ThicknessRoundingRule.Minimum_1_32)
        {
            var gauge = LeadStepTable.First(g => g.Label == "1/32\"");
            floorMm = gauge.Mm;
            floorLabel = gauge.Label;
        }

        // Apply floor (cannot go below floor)
        if (floorMm > 0 && finalMm < floorMm)
        {
            finalMm = floorMm;
            finalLabel = floorLabel;
        }

        // ----------------------------
        // +1 / +2 stepping (never thinner)
        // ----------------------------
        int step = ThicknessRoundingRule switch
        {
            ThicknessRoundingRule.RoundUpPlus1Step => 1,
            ThicknessRoundingRule.RoundUpPlus2Steps => 2,
            _ => 0
        };

        if (step > 0)
        {
            // IMPORTANT: step from the AUTO THICKNESS, not from req
            // so "Auto=1/32" then +1 => next in LeadStep table (e.g., 1/16), not 5/128 weirdness.
            int baseIdx = LeadStepMm.FindIndex(x => x >= autoMm);
            if (baseIdx < 0) baseIdx = LeadStepMm.Count - 1;

            int idx = Math.Min(LeadStepMm.Count - 1, baseIdx + step);

            finalMm = LeadStepMm[idx];
            finalLabel = LeadStepTable[idx].Label;

            // re-apply floor just in case (usually redundant)
            if (floorMm > 0 && finalMm < floorMm)
            {
                finalMm = floorMm;
                finalLabel = floorLabel;
            }
        }

        // ----------------------------
        // Guarantee never thinner than required
        // (autoMm already >= req, floor/step only increases, but keep guard)
        // ----------------------------
        if (finalMm < req)
        {
            // clamp back to auto baseline (safe)
            finalMm = autoMm;
            finalLabel = autoLabel;
        }

        FinalThickness_mm = finalMm;
        FinalThicknessShortDisplay = finalLabel;

        // UI rule: Auto is baseline -> don't show "Final:" line.
        // Everything else shows Final.
        HasThicknessRounding = (ThicknessRoundingRule != ThicknessRoundingRule.Auto);
    }

    public bool ShowPetRegionSelector =>
    _owner is not null &&
    _owner.IsPET &&
    _owner.SelectedRoomCategory is "PET/CT Suite (Uptake + PET)" &&
    Kind == BarrierKind.Secondary;

    public bool ShowSpectRegionSelector =>
    _owner is not null &&
    _owner.IsSpectSuite &&
    Kind == BarrierKind.Secondary;

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPetRegionSelector));
        OnPropertyChanged(nameof(ShowSpectRegionSelector));
    }

    partial void OnHasLinkedSecondaryChanged(bool value)
    {
        if (_owner.IsLoadingProject) return;
        if (Kind != BarrierKind.Primary) return;
        _owner.OnPrimaryLinkedSecondaryToggled(this, value);
    }

    partial void OnHasTwinSecondaryChanged(bool value)
    {
        if (_owner.IsLoadingProject) return;
        if (Kind != BarrierKind.Secondary) return;
        _owner.OnSecondaryTwinToggled(this, value);
    }

    partial void OnIsCtTertiaryChanged(bool value)
    {
        if (value && TertiaryHeight_m <= 0)
            TertiaryHeight_m = 1.0;  // default to 1 m when enabled
    }

    private bool _terHeightGuard;

    partial void OnTertiaryHeight_mChanged(double value)
    {
        if (_terHeightGuard) return;
        _terHeightGuard = true;
        TertiaryHeightText = value == 0 ? "" : value.ToString("0.####", CultureInfo.CurrentCulture);
        _terHeightGuard = false;
    }

    partial void OnTertiaryHeightTextChanged(string value)
    {
        if (_terHeightGuard) return;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ||
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
        {
            _terHeightGuard = true;
            TertiaryHeight_m = v;
            _terHeightGuard = false;
        }
        else if (string.IsNullOrWhiteSpace(value))
        {
            _terHeightGuard = true;
            TertiaryHeight_m = 0;
            _terHeightGuard = false;
        }
    }

    public bool IsK1ReadOnly
    {
        get
        {
            // PET / NM / CT ? always editable
            if (!CanUseNcrpK)
                return false;

            // X-ray ? read-only only when NCRP is used
            return UseNcrpK;
        }
    }

    public void NotifyCTRoomStateChanged()
    {
        OnPropertyChanged(nameof(CanUseNcrpK));
        OnPropertyChanged(nameof(IsK1ReadOnly));
    }
    public void NotifyPetRoomStateChanged()
    {
        OnPropertyChanged(nameof(CanUseNcrpK));
        OnPropertyChanged(nameof(IsK1ReadOnly));
        OnPropertyChanged(nameof(ShowPetRegionSelector));
        OnPropertyChanged(nameof(ShowSpectRegionSelector));
    }

    private void OnK1Edited()
    {
        KSource = "Manual";
        if (IsSelected)
            _owner?.RecomputeBarrier(this);
        OnPropertyChanged(nameof(CommercialGaugeOnly));
        OnPropertyChanged(nameof(ThicknessShortDisplay));
    }

    public void SetK1FromEngine(double value)
    {
        _k1Guard = true;
        try
        {
            K1 = value;
            K1Text = value.ToString("0.####", CultureInfo.CurrentCulture);
            K1ManualOverride = false;   // engine owns it
        }
        finally { _k1Guard = false; }

        OnPropertyChanged(nameof(IsK1ReadOnly));
    }

    partial void OnK1Changed(double value)
    {
        if (_k1Guard) return;
        _k1Guard = true;
        try
        {
            K1ManualOverride = true;
            var s = value.ToString("0.####", CultureInfo.CurrentCulture);
            if (K1Text != s) K1Text = s;
        }
        finally { _k1Guard = false; }

        OnK1Edited();
    }

    partial void OnK1TextChanged(string value)
    {
        if (_k1Guard) return;

        if (string.IsNullOrWhiteSpace(value))
        {
            _k1Guard = true;
            try
            {
              //  K1ManualOverride = true;
                K1 = 0.0;
               // KSource = "Manual";
            }
            finally { _k1Guard = false; }

            // optional: don't recompute on blank (prevents thickness jumping to 0 mid-typing)
            return;
        }

       
        var dec = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (value.EndsWith(dec)) return;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ||
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
        {
            _k1Guard = true;
            try
            {
                K1ManualOverride = true;   // <-- critical: once user types, it is Manual
                K1 = v;
            }
            finally { _k1Guard = false; }

            OnK1Edited();
        }
    }



    public bool IsDrawable => Designation is not BarrierDesignation.Floor
        and not BarrierDesignation.Ceiling;

   public bool IsPreShieldEditable => Kind == BarrierKind.Primary;

    partial void OnUseNcrpKChanged(bool value)
    {
        // If someone tries to turn it on while PET, force it back off.
        if (!CanUseNcrpK && value)
        {
            UseNcrpK = false;
            return;
        }

        OnPropertyChanged(nameof(IsK1ReadOnly));
    }


    // Join only the parts that exist, so an unlabeled barrier doesn't start with a stray dash.
    public string DisplayName =>
        string.Join(" - ", new[] { Label, Kind.ToString(), Designation.ToString(), ProtectedArea }
            .Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>Which limit set the thickness ("Weekly", "Hourly"); empty when not applicable (x-ray, PET, none needed).</summary>
    [ObservableProperty] private string governingLimit = "";


   
    [ObservableProperty] private double labelX;
    [ObservableProperty] private double labelY;

    partial void OnX1Changed(double value)
    {
        UpdateLabelPos(); 
       
    }
    partial void OnY1Changed(double value)
    {
        UpdateLabelPos();
       
    }
    partial void OnX2Changed(double value)
    {
        UpdateLabelPos();
       
    }
    partial void OnY2Changed(double value)
    {
        UpdateLabelPos();
       
    }

    // How far away from the line (in canvas px) the label snaps when you click the line
    [ObservableProperty] private double labelOffset_px = 0.0;

    // Optional: shift along the line (positive = towards X2/Y2, negative = towards X1/Y1)
    [ObservableProperty] private double labelAlong_px = -10.0;

    private void UpdateLabelPos()
    {
        // Midpoint
        var mx = (X1 + X2) / 2.0;
        var my = (Y1 + Y2) / 2.0;

        // Tangent (dx,dy) and unit-length versions
        double dx = X2 - X1, dy = Y2 - Y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6)
        {
            // Degenerate: just put it above the point by the offset
            LabelX = mx;
            LabelY = my - LabelOffset_px;
            return;
        }

        double tx = dx / len;    // unit tangent
        double ty = dy / len;

        // Perpendicular unit normal (rotate tangent by +90°)
        double nx = -ty;
        double ny = tx;
        // Snap: midpoint + (normal * offset) + (tangent * along)
        LabelX = mx + nx * LabelOffset_px + tx * LabelAlong_px + 10;
        LabelY = my + ny * LabelOffset_px + ty * LabelAlong_px;
    }

    public void SyncLabelOffsetAlongFromXY()
    {
        // Midpoint
        var mx = (X1 + X2) / 2.0;
        var my = (Y1 + Y2) / 2.0;

        double dx = X2 - X1, dy = Y2 - Y1;
        double len = Math.Sqrt(dx * dx + dy * dy);

        if (len < 1e-6)
        {
            // degenerate: treat as vertical offset only
            LabelOffset_px = my - LabelY;
            LabelAlong_px = 0;
            return;
        }

        double tx = dx / len, ty = dy / len;   // unit tangent
        double nx = -ty, ny = tx;              // unit normal

        // IMPORTANT: UpdateLabelPos adds +10 to X
        double lx = LabelX - (mx + 10);
        double ly = LabelY - my;

        LabelOffset_px = lx * nx + ly * ny;
        LabelAlong_px = lx * tx + ly * ty;
    }

    public void RefreshLabelPosition() => UpdateLabelPos();

    partial void OnLabelChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        UpdateLabelPos();
    }
    partial void OnKindChanged(BarrierKind value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsPreShieldEditable));
        if (value == BarrierKind.Secondary) ApplyPreShield = false;
    }

    partial void OnDesignationChanged(BarrierDesignation value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDrawable));
        OnPropertyChanged(nameof(VisibleOnCanvas));
        OnPropertyChanged(nameof(EffectiveDistance_m));

        // Auto-label Floor/Ceiling (keeps user edits unless the label was auto-set)
        var nextAuto = value switch
        {
            BarrierDesignation.Floor => "Floor",
            BarrierDesignation.Ceiling => "Ceiling",
            _ => ""
        };

        if (!string.IsNullOrEmpty(nextAuto))
        {
            if (string.IsNullOrWhiteSpace(Label) || (_autoLabelFromDesignation && Label == _lastAutoLabel))
            {
                _autoLabelFromDesignation = true;
                _lastAutoLabel = nextAuto;
                Label = nextAuto;
            }
        }
        else
        {
            // If user switches away from Floor/Ceiling and the label was auto-generated, clear it
            if (_autoLabelFromDesignation && Label == _lastAutoLabel)
            {
                _autoLabelFromDesignation = false;
                _lastAutoLabel = "";
                Label = "";
            }
        }
    }

    partial void OnProtectedAreaChanged(string? value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnMaterialNameChanged(string value)
    {
        OnPropertyChanged(nameof(ThicknessShortDisplay));
        ApplyThicknessRounding();
    }

    private void RefreshName()
    {
        var kindText = Kind == BarrierKind.Primary ? "Primary" : "Secondary";
        var desigText = Designation.ToString();
        var area = string.IsNullOrWhiteSpace(ProtectedArea) ? "(unnamed area)" : ProtectedArea!.Trim();
        var labell = string.IsNullOrWhiteSpace(Label) ? "" : $"{Label} – ";
        Name = $"{labell}{kindText} - {desigText} - {area}";
    }

    public void InitDisplayName() => RefreshName();

    public bool VisibleOnCanvas => IsDrawable && !IsHiddenOnCanvas;
    partial void OnIsHiddenOnCanvasChanged(bool value) => OnPropertyChanged(nameof(VisibleOnCanvas));

    public double ExtraOffset_m => Designation switch
    {
        BarrierDesignation.Ceiling => 0.5,
        BarrierDesignation.Floor => Math.Max(0.0, (_owner.LevelBelowHeight_ft * 0.3048) - 1.7),
        _ => 0.3, // walls/door/window
    };


    // public double EffectiveDistance_m => DistanceOverride_m > 0 ? DistanceOverride_m + ExtraOffset_m : AutoDistance_m;
    public double EffectiveDistance_m => (DistanceOverride_m > 0 ? DistanceOverride_m : AutoDistance_m) + ExtraOffset_m;


    partial void OnDisplayDistance_mChanged(double value)
    {
        OnPropertyChanged(nameof(EffectiveDistance_m));
    }

    partial void OnAutoDistance_mChanged(double value)
    {
        OnPropertyChanged(nameof(EffectiveDistance_m));
    }


    // Proxy string for editing override feet safely (accepts partial input like "7.", "")


    private bool _updatingTextGuard;

    partial void OnDistanceOverride_mChanged(double value)
    {
        // Keep the edit box in sync if value changes elsewhere
        OnPropertyChanged(nameof(EffectiveDistance_m));
        if (_updatingTextGuard) return;
        _updatingTextGuard = true;
        DistanceOverrideFtText = (value / 0.3048).ToString("0.###", CultureInfo.CurrentCulture);
        _updatingTextGuard = false;
    }

    partial void OnDistanceOverrideFtTextChanged(string value)
    {

        // OnPropertyChanged(nameof(DistanceOverrideFtText));
        OnPropertyChanged(nameof(EffectiveDistance_m)); // so the UI updates instantly
        if (_updatingTextGuard) return;
        _updatingTextGuard = true;

        // Try current culture first, then invariant; only commit when parsable.
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var ft) ||
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out ft))
        {
            DistanceOverride_m = Math.Max(0, ft * 0.3048);
        }
        else if (string.IsNullOrWhiteSpace(value))
        {
            DistanceOverride_m = 0; // empty means "use drawing"
        }
        // else: keep previous numeric value while user is still typing "7." etc.

        _updatingTextGuard = false;
    }

    partial void OnZoneChanged(string value)
    {
        if (!POverridden)
        {
            _suppressPOverride = true;
            PMGyWeek = string.Equals(value, "Controlled", StringComparison.OrdinalIgnoreCase) ? 0.1 : 0.02;
            _suppressPOverride = false;
        }
    }

    partial void OnPMGyWeekChanged(double value)
    {
        if (!_suppressPOverride)
            POverridden = true;
    }

    private bool IsLead =>
        (MaterialName?.IndexOf("lead", StringComparison.OrdinalIgnoreCase) >= 0
         && MaterialName?.IndexOf("glass", StringComparison.OrdinalIgnoreCase) < 0)
        || string.Equals(MaterialName, "Pb", StringComparison.OrdinalIgnoreCase);

    public string CommercialGaugeOnly =>
    double.IsPositiveInfinity(RequiredThickness_mm) ? "Not solvable" :
    !double.IsFinite(RequiredThickness_mm) || RequiredThickness_mm <= 0 ? "0\"" :
    IsLead
        ? GaugeUtils.RoundUpGauge(RequiredThickness_mm).Label
        : $"{RequiredThickness_mm / 25.4:0.###}\"";


    public string GaugeKey => CommercialGaugeOnly;

    public string ThicknessShortDisplay
    {
        get
        {
            if (double.IsPositiveInfinity(RequiredThickness_mm))
                return "Not solvable by wall thickening";

            if (!double.IsFinite(RequiredThickness_mm) || RequiredThickness_mm <= 0)
                return "0 mm / 0\"";

            string matAbbrev =
                MaterialName?.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 ? "lead glass" :
                MaterialName?.IndexOf("lead", StringComparison.OrdinalIgnoreCase) >= 0 ? "Pb" :
                MaterialName?.IndexOf("concrete", StringComparison.OrdinalIgnoreCase) >= 0 ? "conc." :
                MaterialName ?? "";

            if (IsLead)
            {
                var g = GaugeUtils.RoundUpGauge(RequiredThickness_mm);
                return $"{RequiredThickness_mm:F3} mm {matAbbrev} ({g.Label})";
            }
            else
            {
                var inches = RequiredThickness_mm / 25.4;
                return $"{RequiredThickness_mm:F3} mm {matAbbrev} ({inches:0.###}\")";
            }
        }
    }

    // keep legend & colors live when thickness changes
    partial void OnRequiredThickness_mmChanged(double value)
    {
        OnPropertyChanged(nameof(CommercialGaugeOnly));
        OnPropertyChanged(nameof(GaugeKey));
        OnPropertyChanged(nameof(ThicknessShortDisplay));
        ApplyThicknessRounding();
    }



    // === Pre-existing shields (new) ===
    public ObservableCollection<PreexistingLayerViewModel> PreexistingLayers { get; } = new();

    // “Add new layer” scratch inputs (bound to the add row)
    [ObservableProperty] private string newPreMat = "None";
    [ObservableProperty] private double newPreThk = 0.0;
    [ObservableProperty] private string newPreUnit = "mm";

    public void AddPreLayer()
    {
        if (NewPreThk <= 0) return;
        if (string.IsNullOrWhiteSpace(NewPreMat) ||
            string.Equals(NewPreMat, "None", StringComparison.OrdinalIgnoreCase))
            return;

        PreexistingLayers.Add(new PreexistingLayerViewModel
        {
            MaterialName = NewPreMat!,
            ThicknessValue = NewPreThk,
            ThicknessUnit = NewPreUnit
        });
        NewPreThk = 0.0; // leave material/unit sticky
    }


    public void RemovePreLayer(PreexistingLayerViewModel? layer)
    {
        if (layer is null) return;
        PreexistingLayers.Remove(layer);
    }

    public Func<BarrierItemViewModel, double>? GetCanvasDistance_m { get; set; }


    private bool _saveDistance;
    public bool SaveDistance
    {
        get => _saveDistance;
        set
        {
            if (SetProperty(ref _saveDistance, value))
            {
                if (value)
                {
                    // Prefer rule distance for Floor/Ceiling; otherwise use canvas distance
                    var rule = _owner.GetRuleDistance_m(this);
                    var raw = rule ?? (GetCanvasDistance_m?.Invoke(this) ?? DisplayDistance_m);

                    if (raw > 0)
                        SavedRawDistance_m = raw;

                    DisplayDistance_m = SavedRawDistance_m; // lock immediately
                    AutoDistance_m = SavedRawDistance_m;    // base-only
                }

                OnPropertyChanged(nameof(EffectiveDistance_m));
            }
        }
    }



    private double _savedRawDistance_m;
    public double SavedRawDistance_m
    {
        get => _savedRawDistance_m;
        set => SetProperty(ref _savedRawDistance_m, value);
    }

    public void RaiseEffectiveDistanceChanged()
    {
        OnPropertyChanged(nameof(EffectiveDistance_m));
    }

    public void LoadK1FromDto(double k1, bool manualOverride)
    {
        _k1Guard = true;
        try
        {
            K1ManualOverride = manualOverride;
            K1 = k1;
            K1Text = k1.ToString("0.####", CultureInfo.CurrentCulture);
        }
        finally { _k1Guard = false; }

        // Keep UI state consistent
        OnPropertyChanged(nameof(IsK1ReadOnly));
    }

    public bool IsK1InputEnabled => K1ManualOverride;

    partial void OnK1ManualOverrideChanged(bool value)
    {
        if (_k1Guard) return;

        if (value)
        {
            // Manual ON: do not let engine overwrite, show Manual label
            KSource = "Manual";
        }
        else
        {
            // Manual OFF: clear manual entry so next Compute can repopulate from auto method
            _k1Guard = true;
            try
            {
                K1 = 0.0;
                K1Text = "";
            }
            finally { _k1Guard = false; }
        }

        // So XAML bindings refresh immediately
        OnPropertyChanged(nameof(IsK1InputEnabled));
    }

    // Cache last compute trace so selecting a barrier can show it instantly
    public ObservableCollection<KeyValuePair<string, string>> CachedTraceItems { get; } = new();

    [ObservableProperty] private string cachedResultSummary = "";



}



