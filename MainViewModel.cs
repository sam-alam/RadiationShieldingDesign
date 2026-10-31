using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
//using DocumentFormat.OpenXml.Wordprocessing;

//using DocumentFormat.OpenXml.InkML;

//using DocumentFormat.OpenXml.Drawing;
using RadiationShieldingDesign.Engine;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
//using System.Windows.Media.Imaging;

//using static RadiationShieldingDesign.MainWindow;
using Path = System.IO.Path;

namespace RadiationShieldingDesign;


public partial class MainViewModel : ObservableObject
{
    private readonly NcrpDataStore _data;

    private IRelayCommand? _startDesignCommand;

    // simple A, B, C, ... AA auto-suggester
    private int _labelCounter;

    private static string ToAlpha(int n)
    {
        // 0->A, 25->Z, 26->AA ...
        n = Math.Max(0, n);
        var s = "";
        while (true)
        {
            s = (char)('A' + n % 26) + s;
            n = n / 26 - 1;
            if (n < 0) break;
        }

        return s;
    }

    private string SuggestLabel()
    {
        return ToAlpha(_labelCounter++);
    }

    [ObservableProperty] private double canvasWidth = 2400; // tweak as you like
    [ObservableProperty] private double canvasHeight = 1800; // tall enough to show a scrollbar initially

    // ===== Canvas / scale / tube =====
    [ObservableProperty] private double cmPerPixel = 0.2; // user scale (cm per pixel)
    [ObservableProperty] private double tubeX = 500; // tube position on canvas
    [ObservableProperty] private double tubeY = 420;

    // For measure-scale overlay
    [ObservableProperty] private bool isMeasuringScale = false;
    [ObservableProperty] private double measureX1, measureY1, measureX2, measureY2;

    [ObservableProperty] private bool showBarrierLabels = true;

    public ObservableCollection<string> ZoneTypes { get; } = new() { "Uncontrolled", "Controlled" };

    // Subscribe to selected barrier for auto-updates
    private BarrierItemViewModel? _subscribedBarrier;

    // ===== Data-driven lookups =====
    public ObservableCollection<string> MaterialNames { get; } = new();
    public ObservableCollection<string> PreShieldTypes { get; } = new();

    public ObservableCollection<string> PreexistingMaterialNames { get; } =
        new() { "None", "Lead", "Std Concrete", "Lite Concrete", "Steel", "Gypsum", "Wood", "Lead-Eq" };


    public ObservableCollection<string> ThicknessUnits { get; } =
        new() { "mm", "cm", "in" };

    // ===== Room categories (used to load default layout & scenarios) =====
    public ObservableCollection<string> RoomCategories { get; } = new()
    {
        "Radiography",
        "Dedicated Chest Radiography",
        "Rad Tube (R&F room)",
        "Fluoro Tube (R&F room)",
        "C-arm",
        "Mammography",
        "Cardiac Angio",
        "Peripheral Angio",
        "CT",
        "PET/CT (PET room)",
        "PET Uptake room",
        "PET/CT Suite (Uptake + PET)"
    };

    public IReadOnlyList<BarrierDesignation> AllDesignations { get; } =
        new[]
        {
            BarrierDesignation.ChestBuckyWall,
            BarrierDesignation.CrossTableLateralWall,
            BarrierDesignation.Wall,
            BarrierDesignation.Window,
            BarrierDesignation.Door,
            BarrierDesignation.Floor,
            BarrierDesignation.Ceiling
        };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDesignCommand))]
    private string? selectedRoomCategory; // null at startup

    public IReadOnlyList<double> OccupancyChoices { get; } = new[] { 1.0, 0.5, 0.2, 0.125, 0.05, 0.025 };

    // ===== Barrier collection =====
    public ObservableCollection<BarrierItemViewModel> Barriers { get; } = new();

    [ObservableProperty] private BarrierItemViewModel? selectedBarrier;


    // ===== Room parameters tab (ft unless noted) =====
    [ObservableProperty] private double roomHeight_ft = 15.0;

    [ObservableProperty]
    private double levelBelowHeight_ft = 12.0;

    [ObservableProperty] private double chestBuckyToWall_ft = 2.5;

    [ObservableProperty] private double sourceToFloor_ft = 7.0;
    [ObservableProperty] private double sourceToCeiling_ft = 6.0;

    // ===== Conversions tab =====
    [ObservableProperty] private string convFeetText = "7.0";
    [ObservableProperty] private string convInchesText = "4";
    [ObservableProperty] private string convMetersText = "";

    [ObservableProperty] private bool isMeasuringDistance = false;

    public IRelayCommand AddPreLayerCommand { get; }
    public IRelayCommand<PreexistingLayerViewModel> RemovePreLayerCommand { get; }
    [ObservableProperty] private bool useNcrpAverage;
    [ObservableProperty] private bool useNcrpBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDesignCommand))]
    private bool hasActiveDesign;

    [ObservableProperty] private double patientsPerWeek = 60;
    [ObservableProperty] private string patientsPerWeekText = "60";
    [ObservableProperty] private double zoom = 1.0;
    public RelayCommand<object> SetGapCommand { get; private set; }

    [ObservableProperty] private ImageSource? backgroundImage;
    [ObservableProperty] private double backgroundOpacity = 0.20;
    [ObservableProperty] private double backgroundScale = 1.0;
    [ObservableProperty] private double backgroundOffsetX = 0.0;
    [ObservableProperty] private double backgroundOffsetY = 0.0;

    // simple toggle so the mouse code knows whether to move bg instead of barriers/tube
    [ObservableProperty] private bool isEditingBackground = false;
    // User-entered room name (e.g., "TC-307", "X-ray 1", "575 Lex DR1")
    [ObservableProperty] private string roomName = "";

    public ObservableCollection<string> MaterialNamesPet { get; } = new()
{
    "Lead",
    "Concrete",
    "Iron",
};
    public bool IsPET =>
    string.Equals(SelectedRoomCategory, "PET/CT (PET room)", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(SelectedRoomCategory, "PET Uptake room", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(SelectedRoomCategory, "PET/CT Suite (Uptake + PET)", StringComparison.OrdinalIgnoreCase);
    public IEnumerable<string> MaterialNamesForUi => IsPET ? MaterialNamesPet : MaterialNames;


    [ObservableProperty]
    private PetIsotope selectedIsotope = PetIsotope.F18;

    // administered activity (MBq)
    [ObservableProperty] private double petAdmin_MBq = 370.0;
    [ObservableProperty] private string petAdmin_MBq_Text = "370";

    // uptake time (min) and imaging time (min)
    [ObservableProperty] private double petUptake_min = 60.0;
    [ObservableProperty] private string petUptake_min_Text = "60";

    [ObservableProperty] private double petImaging_min = 20.0;
    [ObservableProperty] private string petImaging_min_Text = "20";

    [ObservableProperty] private double petVoidingFactor = 0.85;
    [ObservableProperty] private string petVoidingFactor_Text = "0.85";
    [ObservableProperty] private bool showAllLabels = true;

    public MainViewModel()
    {
        // Load data bundle
        HasActiveDesign = false;
        _startDesignCommand = new RelayCommand(ExecuteStartDesign, CanStartDesign);
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var bundlePath = Path.Combine(baseDir, "Data", "ncrp147_data_bundle.json");
        if (!File.Exists(bundlePath))
        {
            var alt = Path.Combine(baseDir, "data", "ncrp147_data_bundle.json");
            if (File.Exists(alt)) bundlePath = alt;
        }

        try
        {
            _data = NcrpDataStore.LoadFromBundle(bundlePath);
            NcrpOccupancyRows.Clear();
            foreach (var r in _data.Occupancy) NcrpOccupancyRows.Add(r);

            NcrpPrimaryAirKermaRows.Clear();
            foreach (var r in _data.PrimaryAirKerma) NcrpPrimaryAirKermaRows.Add(r);

            NcrpSecondaryAirKermaRows.Clear();
            foreach (var r in _data.SecondaryAirKerma) NcrpSecondaryAirKermaRows.Add(r);

            
            PrimaryCoeffTable = BuildCoeffTable(_data.PrimaryBarrierCoeffs);
            SecondaryCoeffTable = BuildCoeffTable(_data.SecondaryBarrierCoeffs);
            CTCoeffTable = BuildCTCoeffTable(_data.CTCoeffs);
            // NCRP 147 Table 5.2 style “planning defaults” (quick reference)
            CtProcedureDefaults.Clear();
            CtProcedureDefaults.Add(new CtProcedureDefaultRow { Procedure = "Head", CTDIvol_mGy = 60, ScanLength_cm = 20, DLP_mGy_cm = 1200 });
            CtProcedureDefaults.Add(new CtProcedureDefaultRow { Procedure = "Chest", CTDIvol_mGy = 15, ScanLength_cm = 35, DLP_mGy_cm = 525 });
            CtProcedureDefaults.Add(new CtProcedureDefaultRow { Procedure = "Abdomen", CTDIvol_mGy = 25, ScanLength_cm = 25, DLP_mGy_cm = 625 });
            CtProcedureDefaults.Add(new CtProcedureDefaultRow { Procedure = "Pelvis", CTDIvol_mGy = 25, ScanLength_cm = 20, DLP_mGy_cm = 500 });
            CtProcedureDefaults.Add(new CtProcedureDefaultRow { Procedure = "Body avg (C/A/P)", CTDIvol_mGy = 0, ScanLength_cm = 0, DLP_mGy_cm = 550 });

        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load data: " + ex.Message);
            _data = new NcrpDataStore();
        }

        // Materials for drop-down
        foreach (var name in _data.Materials.Keys.OrderBy(s => s == "Lead" ? 0 : 1).ThenBy(s => s))
            MaterialNames.Add(name);

        // Pre-shield menu
        PreShieldTypes.Clear();
        foreach (var spec in _data.PrimaryPreShield.Values)
            PreShieldTypes.Add(spec.Type);

        // Commands
        AddPrimaryCommand = new RelayCommand(AddPrimaryBarrier);
        AddSecondaryCommand = new RelayCommand(AddSecondaryBarrier);
        //RemoveSelectedCommand = new RelayCommand(() =>
        //{
        //    if (SelectedBarrier is BarrierItemViewModel b)
        //    {
        //        b.Dispose();
        //        Barriers.Remove(b);
        //        if (ReferenceEquals(SelectedBarrier, b)) SelectedBarrier = null;
        //        UpdateCanvasExtent();
        //    }

        //});
        RemoveSelectedCommand = new RelayCommand(() =>
        {
            if (SelectedBarrier is not BarrierItemViewModel b) return;

            // If THIS barrier has linked children, remove them first
            if (b.LinkedSecondaryId is Guid sid) RemoveBarrierById(sid);
            if (b.TwinSecondaryId is Guid tid) RemoveBarrierById(tid);

            // If THIS barrier is itself a linked child, RemoveBarrierById will clear the parent's toggle
            var id = b.Id;
            RemoveBarrierById(id);

            UpdateCanvasExtent();
        });

        ComputeSelectedCommand = new RelayCommand(() =>
        {
            if (SelectedBarrier != null) ComputeBarrier(SelectedBarrier);
        });
        ComputeAllCommand = new RelayCommand(() =>
        {
            foreach (var b in Barriers) ComputeBarrier(b);
        });

        StartMeasureScaleCommand = new RelayCommand(() => IsMeasuringScale = true);
        StartMeasureDistanceCommand = new RelayCommand(() => IsMeasuringDistance = true);


        AddPreLayerCommand = new RelayCommand(() =>
        {
            if (SelectedBarrier == null) return;
            SelectedBarrier.AddPreLayer();
            ComputeBarrier(SelectedBarrier); // immediate feedback
        });

        RemovePreLayerCommand = new RelayCommand<PreexistingLayerViewModel>(pl =>
        {
            if (SelectedBarrier == null) return;
            SelectedBarrier.RemovePreLayer(pl);
            ComputeBarrier(SelectedBarrier); // immediate feedback
        });

      
        SetGapCommand = new RelayCommand<object>(OnSetGap);

        PropertyChanged += (_, e) =>
        {
            if (_suspendDirty) return;

            // Ignore stuff that changes constantly or is derived
            if (e.PropertyName is nameof(IsDirty) or nameof(CurrentProjectPath)) return;
            if (e.PropertyName is nameof(ConvFeetText) or nameof(ConvInchesText) or nameof(ConvMetersText)) return;

            MarkDirty();
        };

        using (SuspendDirty())
        {
            SurveyRate_mR_per_h_Text = SurveyRate_mR_per_h.ToString("G", CultureInfo.CurrentCulture);
            SurveyOnTime_h_per_week_Text = SurveyOnTime_h_per_week.ToString("G", CultureInfo.CurrentCulture);
            SurveyPerExam_mR_Text = SurveyPerExam_mR.ToString("G", CultureInfo.CurrentCulture);
            SurveyExams_per_week_Text = SurveyExams_per_week.ToString("G", CultureInfo.CurrentCulture);
        }
        ClearDirty();

        Barriers.CollectionChanged += Barriers_CollectionChanged;

        // existing barriers (if any)
        foreach (var b in Barriers)
            HookBarrierDirty(b);

        // Seed initial scrollable height
        UpdateCanvasExtent();

        // Auto-update when items are added/removed
        Barriers.CollectionChanged += (s, e) =>
        {
            // attach/detach move listeners so extent grows when a barrier moves
            if (e.NewItems != null)
                foreach (BarrierItemViewModel nb in e.NewItems)
                    nb.PropertyChanged += BarrierOnPropertyChanged;

            if (e.OldItems != null)
                foreach (BarrierItemViewModel ob in e.OldItems)
                    ob.PropertyChanged -= BarrierOnPropertyChanged;

            UpdateCanvasExtent();
        };

        RecomputeDistancesForAll();


    }

    public IEnumerable<PetIsotope> PetIsotopeOptions =>
    Enum.GetValues(typeof(PetIsotope)).Cast<PetIsotope>();

    private void RecomputeIfPET()
    {
        if (!IsPET) return;
        if (SelectedBarrier != null) ComputeBarrier(SelectedBarrier);
    }

    partial void OnPetAdmin_MBq_TextChanged(string value) => PetAdmin_MBq = ParseOrZero(value);
    partial void OnPetUptake_min_TextChanged(string value) => PetUptake_min = ParseOrZero(value);
    partial void OnPetImaging_min_TextChanged(string value) => PetImaging_min = ParseOrZero(value);
    partial void OnPetVoidingFactor_TextChanged(string value) => PetVoidingFactor = ParseOrZero(value);

    partial void OnSelectedIsotopeChanged(PetIsotope value)
    {
        RecomputeIfPET();
    }

    partial void OnPetAdmin_MBqChanged(double value) { PetAdmin_MBq_Text = value.ToString("0.####", CultureInfo.CurrentCulture); RecomputeIfPET(); }
    partial void OnPetUptake_minChanged(double value) { PetUptake_min_Text = value.ToString("0.####", CultureInfo.CurrentCulture); RecomputeIfPET(); }
    partial void OnPetImaging_minChanged(double value) { PetImaging_min_Text = value.ToString("0.####", CultureInfo.CurrentCulture); RecomputeIfPET(); }
    partial void OnPetVoidingFactorChanged(double value) { PetVoidingFactor_Text = value.ToString("0.####", CultureInfo.CurrentCulture); RecomputeIfPET(); }


    private bool _isExporting;
    public bool IsExporting
    {
        get => _isExporting;
        set => SetProperty(ref _isExporting, value);
    }

    public void RecomputeBarrier(BarrierItemViewModel b)
    {
        if (b is null) return;
        ComputeBarrier(b);   // calls your existing private method
    }

    private void EnforcePetMaterialOnAllBarriersIfNeeded()
    {
        if (!IsPET) return;

        foreach (var b in Barriers)
        {
            if (b == null) continue;

            // if current material isn't allowed, force to Lead
            if (!MaterialNamesPet.Contains(b.MaterialName))
                b.MaterialName = "Lead";
        }
    }

    public bool IsCT => string.Equals(SelectedRoomCategory, "CT", StringComparison.OrdinalIgnoreCase);

    private void ApplyCtAutoMap()
    {
        if (!IsCT) return;

        // Map CT iso-center to unified distances
        // Assumes you already have CtIsoCenterHeight_ft and RoomHeight_ft
        SourceToFloor_ft = CtIsoCenterHeight_ft;
        SourceToCeiling_ft = Math.Max(0.0, RoomHeight_ft - CtIsoCenterHeight_ft);
    }


    // In CT, these will typically be set by ApplyCtAutoMap().
    partial void OnSourceToFloor_ftChanged(double value)
    {
        RecomputeDistancesForAll();
    }

    partial void OnSourceToCeiling_ftChanged(double value)
    {
        RecomputeDistancesForAll();
    }


    // Start denser scale: more pixels per cm
    partial void OnHasActiveDesignChanged(bool value)
    {
        if (value)
        {
            if (CmPerPixel <= 0) CmPerPixel = 0.20; // 0.20 cm/px => ~152 px/ft
            UpdateCanvasExtent();
        }
    }

    private void ResetToDefaults()
    {

        if (IsXrayRoom)
        {
            UseNcrpAverage = true;
            UseNcrpBusy = false;
            UseNcrpWorkloadNormalization = false;
        }
    }

    public void UpdateCanvasExtent()
    {
        const double pad = 200.0; // breathing room around content

        double maxX = Barriers.Where(b => b != null)
            .Select(b => Math.Max(b.X1, b.X2))
            .DefaultIfEmpty(0.0).Max();
        double maxY = Barriers.Where(b => b != null)
            .Select(b => Math.Max(b.Y1, b.Y2))
            .DefaultIfEmpty(0.0).Max();

        CanvasWidth = Math.Max(CanvasWidth, maxX + pad);
        CanvasHeight = Math.Max(CanvasHeight, maxY + pad);
    }


    private void BarrierOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not BarrierItemViewModel) return;

        // Any coordinate or label move can change the needed extent
        if (e.PropertyName is nameof(BarrierItemViewModel.X1)
            or nameof(BarrierItemViewModel.Y1)
            or nameof(BarrierItemViewModel.X2)
            or nameof(BarrierItemViewModel.Y2)
            or nameof(BarrierItemViewModel.LabelX)
            or nameof(BarrierItemViewModel.LabelY))
            UpdateCanvasExtent();

        if (e.PropertyName is nameof(BarrierItemViewModel.DistanceOverride_m)
            or nameof(BarrierItemViewModel.Designation))
            RecomputeDistancesForAll();
    }


    public IRelayCommand StartDesignCommand =>
        _startDesignCommand ??= new RelayCommand(ExecuteStartDesign, CanStartDesign);

    // keep the Can/Execute methods cleanly separated
    private bool CanStartDesign()
    {
       return !HasActiveDesign && !string.IsNullOrWhiteSpace(SelectedRoomCategory);
    }

    private void ExecuteStartDesign()
    {
        if (!CanStartDesign()) return;

        HasActiveDesign = true;
        ResetToDefaults();
        LoadDefaultLayoutForRoom();
        ApplyCategoryExamPresets();
        ApplyNcrpPatientsIfLocked();

        if (IsCT)
        {
            ApplyCtDefaults();
            if (string.IsNullOrEmpty(CtKvp)) CtKvp = "120";
        }
        if (IsPET)
        {
            foreach (var b in Barriers)
            {
                b.UseNcrpK = false;
                b.SetK1FromEngine(0.0);
                b.KSource = "PET";
                b.NotifyPetRoomStateChanged();
            }
        }
    }


    private IRelayCommand? _clearDesignCommand;

    public IRelayCommand ClearDesignCommand => _clearDesignCommand ??= new RelayCommand(() =>
    {
        var res = MessageBox.Show(
            "This will clear the current layout and return to the first screen. Continue?",
            "Clear design",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (res != MessageBoxResult.Yes) return;

        if (IsLoadingProject) return;

        UseNcrpAverage = false;
        UseNcrpBusy = false;
        UseNcrpWorkloadNormalization = false;
        Barriers.Clear();
        SelectedBarrier = null;
        ResultSummary = "—";
        SelectedRoomCategory = null;
        TraceItems.Clear();
        HasActiveDesign = false;
        NotifySurveyChanged();
        BackgroundImage = null;
        BackgroundOpacity = 0.0;
        BackgroundScale = 1.0;
        BackgroundOffsetX = 0;
        BackgroundOffsetY = 0;
        IsEditingBackground = false;
        IsDirty = false;
    });


    // CT panel values (Parameters tab)
    [ObservableProperty] private string ctKvp = "120"; // "120" | "140"
    [ObservableProperty] private double ctIsoCenterHeight_ft = 3.35;
    [ObservableProperty] private double ctDlpHeadPerPatient_mGycm = 0.0;

    [ObservableProperty] private double ctDlpBodyPerPatient_mGycm = 0.0;

    [ObservableProperty] private string ctDlpHeadPerPatient_mGycm_Text = "0.0";

    [ObservableProperty] private string ctDlpBodyPerPatient_mGycm_Text = "0.0";

    [ObservableProperty] private bool ctUseNcrpCoeffs = true;

    public enum CtCalcMode
    {
        ScatterMapOnly,
        DlpPlusSutton,
        ScatterMapPlusSuttonSum
    }

    [ObservableProperty] private CtCalcMode ctMode = CtCalcMode.ScatterMapOnly;

    public static IReadOnlyList<KeyValuePair<string, CtCalcMode>> CtModeOptions { get; } =
        new List<KeyValuePair<string, CtCalcMode>>
        {
        new("Scatter map only", CtCalcMode.ScatterMapOnly),
        new("DLP + tertiary Sutton", CtCalcMode.DlpPlusSutton),
        new("Scatter map + tertiary Sutton", CtCalcMode.ScatterMapPlusSuttonSum),
        };


    partial void OnCtModeChanged(CtCalcMode value)
    {
        if (value == CtCalcMode.ScatterMapOnly)
            foreach (var b in Barriers) b.IsCtTertiary = false;
        RecomputeIfCT();
    }


    [ObservableProperty] private double ctHeadScansPerWeek = 0;
    [ObservableProperty] private double ctBodyScansPerWeek = 0;

    [ObservableProperty] private string ctHeadScansPerWeekText = "0";
    [ObservableProperty] private string ctBodyScansPerWeekText = "0";
    [ObservableProperty] private bool ctApplyContrastWeight = false;
    [ObservableProperty] private double ctContrastPercent = 0.0;
    [ObservableProperty] private string ctContrastPercentText = "0";

    public double CtDlpExamCountForWeighting =>
        Math.Max(0.0, CtHeadScansPerWeek + CtBodyScansPerWeek);

    public double CtHeadFractionForDlp =>
        CtDlpExamCountForWeighting > 0 ? CtHeadScansPerWeek / CtDlpExamCountForWeighting : 0.0;

    public double CtBodyFractionForDlp =>
        CtDlpExamCountForWeighting > 0 ? CtBodyScansPerWeek / CtDlpExamCountForWeighting : 0.0;

    public double CtAvgDlpPerExam_Base_mGycm =>
        CtDlpExamCountForWeighting > 0
            ? ((CtBodyFractionForDlp * CtDlpBodyPerPatient_mGycm) +
               (CtHeadFractionForDlp * CtDlpHeadPerPatient_mGycm))
            : 0.0;

    public double CtContrastFactor =>
        CtApplyContrastWeight ? (1.0 + Math.Max(0.0, CtContrastPercent) / 100.0) : 1.0;

    public double CtAvgDlpPerExam_Adjusted_mGycm =>
        CtAvgDlpPerExam_Base_mGycm * CtContrastFactor;

    public double CtDlpWeek_Total_Base_mGycm =>
        (CtDlpHeadPerPatient_mGycm * CtHeadScansPerWeek) +
        (CtDlpBodyPerPatient_mGycm * CtBodyScansPerWeek);

    public double CtDlpWeek_Total_Adjusted_mGycm =>
        CtDlpWeek_Total_Base_mGycm * CtContrastFactor;


    public double CtDlpWeek_Head
    {
        get
        {
            if (!IsCT) return 0.0;
            if (_data?.CTDlpCoeffs is not var (head, body)) return 0.0;

            var K1_head_pt = CtDlpToK.FromDlp(CtDlpHeadPerPatient_mGycm, head.K1_coeff, head.K_per_cm);
            var r = CurrentBarrierDistance_m;
            return K1_head_pt * CtHeadScansPerWeek * CtContrastFactor / (r * r);
        }
    }

    public double CtDlpWeek_Body
    {
        get
        {
            if (!IsCT) return 0.0;
            if (_data?.CTDlpCoeffs is not var (head, body)) return 0.0;

            var K1_body_pt = CtDlpToK.FromDlp(CtDlpBodyPerPatient_mGycm, body.K1_coeff, body.K_per_cm);
            var r = CurrentBarrierDistance_m;
            return K1_body_pt * CtBodyScansPerWeek * CtContrastFactor / (r * r);
        }
    }

    public double CtDlpWeek_Total_Unscaled => CtDlpWeek_Total_Adjusted_mGycm;
    public double CtDlpWeek_Total => CtDlpWeek_Head + CtDlpWeek_Body;
    public bool CtHasDlpWeek => CtDlpWeek_Total_Unscaled > 0;
    public double CtScansPerWeekTotal => Math.Max(0, CtHeadScansPerWeek + CtBodyScansPerWeek);

    partial void OnCtHeadScansPerWeekTextChanged(string value)
    {
        CtHeadScansPerWeek = ParseOrZero(value);
    }
    partial void OnCtBodyScansPerWeekTextChanged(string value)
    {
        CtBodyScansPerWeek = ParseOrZero(value);
    }

    partial void OnCtHeadScansPerWeekChanged(double value)
    {
        CtHeadScansPerWeekText = value.ToString("0.####", CultureInfo.CurrentCulture);
        OnPropertyChanged(nameof(CtHeadScansPerWeekText));

        OnPropertyChanged(nameof(CtScansPerWeekTotal));
        OnPropertyChanged(nameof(CtDlpExamCountForWeighting));
        OnPropertyChanged(nameof(CtHeadFractionForDlp));
        OnPropertyChanged(nameof(CtBodyFractionForDlp));
        OnPropertyChanged(nameof(CtAvgDlpPerExam_Base_mGycm));
        OnPropertyChanged(nameof(CtAvgDlpPerExam_Adjusted_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Base_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Adjusted_mGycm));

        OnPropertyChanged(nameof(CtDlpWeek_Head));
        OnPropertyChanged(nameof(CtDlpWeek_Body));
        OnPropertyChanged(nameof(CtDlpWeek_Total));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Unscaled));
        OnPropertyChanged(nameof(CtHasDlpWeek));

        RaiseCtDlpWeekChanged();
        RecomputeIfCT();
    }
    partial void OnCtBodyScansPerWeekChanged(double value)
    {
        CtBodyScansPerWeekText = value.ToString("0.####", CultureInfo.CurrentCulture);
        OnPropertyChanged(nameof(CtBodyScansPerWeekText));

        OnPropertyChanged(nameof(CtScansPerWeekTotal));
        OnPropertyChanged(nameof(CtDlpExamCountForWeighting));
        OnPropertyChanged(nameof(CtHeadFractionForDlp));
        OnPropertyChanged(nameof(CtBodyFractionForDlp));
        OnPropertyChanged(nameof(CtAvgDlpPerExam_Base_mGycm));
        OnPropertyChanged(nameof(CtAvgDlpPerExam_Adjusted_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Base_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Adjusted_mGycm));

        OnPropertyChanged(nameof(CtDlpWeek_Head));
        OnPropertyChanged(nameof(CtDlpWeek_Body));
        OnPropertyChanged(nameof(CtDlpWeek_Total));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Unscaled));
        OnPropertyChanged(nameof(CtHasDlpWeek));

        RaiseCtDlpWeekChanged();
        RecomputeIfCT();
    }

    partial void OnCtApplyContrastWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(CtContrastFactor));
        OnPropertyChanged(nameof(CtAvgDlpPerExam_Base_mGycm));
        OnPropertyChanged(nameof(CtAvgDlpPerExam_Adjusted_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Base_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Adjusted_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Unscaled));
        OnPropertyChanged(nameof(CtHasDlpWeek));
        OnPropertyChanged(nameof(CtDlpWeek_Head));
        OnPropertyChanged(nameof(CtDlpWeek_Body));
        OnPropertyChanged(nameof(CtDlpWeek_Total));
        RaiseCtDlpWeekChanged();
        RecomputeIfCT();
    }

    partial void OnCtContrastPercentChanged(double value)
    {
        CtContrastPercentText = value.ToString("0.####", CultureInfo.CurrentCulture);
        OnPropertyChanged(nameof(CtContrastPercentText));

        OnPropertyChanged(nameof(CtContrastFactor));
        OnPropertyChanged(nameof(CtAvgDlpPerExam_Base_mGycm));
        OnPropertyChanged(nameof(CtAvgDlpPerExam_Adjusted_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Base_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Adjusted_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Unscaled));
        OnPropertyChanged(nameof(CtHasDlpWeek));
        RaiseCtDlpWeekChanged();
        RecomputeIfCT();
    }

    partial void OnCtContrastPercentTextChanged(string value)
    {
        CtContrastPercent = ParseOrZero(value);
    }

    private double CurrentBarrierDistance_m
    {
        get
        {
            var b = SelectedBarrier;
            if (b is null) return 1.0;
            var r = b.EffectiveDistance_m;
            if (!(r > 0)) r = GetRuleDistance_m(b) ?? 1.0;
            return r;
        }
    }

    public void ApplyCtDefaults()
    {
        if (!IsCT || _data?.CTDefaults is null) return;

        foreach (var b in Barriers)
        {
            b.UseNcrpK = false;
            b.KSource = "CT";
            b.SetK1FromEngine(0.0);
            b.NotifyCTRoomStateChanged();
        }

        if (CtHeadScansPerWeek <= 0)
            CtHeadScansPerWeek = _data.CTDefaults.dlp_per_patient_head_week;

        if (CtBodyScansPerWeek <= 0)
            CtBodyScansPerWeek = _data.CTDefaults.dlp_per_patient_body_week;

        if (CtDlpBodyPerPatient_mGycm <= 0)
            CtDlpBodyPerPatient_mGycm = _data.CTDefaults.dlp_per_patient_body_mGy_cm;

        if (CtDlpHeadPerPatient_mGycm <= 0)
            CtDlpHeadPerPatient_mGycm = _data.CTDefaults.dlp_per_patient_head_mGy_cm;

        if (CtIsoCenterHeight_ft <= 0)
            CtIsoCenterHeight_ft = _data.CTDefaults.iso_center_height_ft;
    }

    // Recompute current barrier whenever CT inputs change (and we’re in a CT room).
    private void RecomputeIfCT()
    {
        if (!IsCT) return;
        if (SelectedBarrier != null) ComputeBarrier(SelectedBarrier);
    }


    partial void OnCtDlpHeadPerPatient_mGycmChanged(double value)
    {
        CtDlpHeadPerPatient_mGycm_Text = value.ToString("0.####", CultureInfo.CurrentCulture);
        OnPropertyChanged(nameof(CtDlpHeadPerPatient_mGycm_Text));

        OnPropertyChanged(nameof(CtHeadFractionForDlp));
        OnPropertyChanged(nameof(CtBodyFractionForDlp));
        OnPropertyChanged(nameof(CtAvgDlpPerExam_Base_mGycm));
        OnPropertyChanged(nameof(CtAvgDlpPerExam_Adjusted_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Base_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Adjusted_mGycm));

        OnPropertyChanged(nameof(CtDlpWeek_Head));
        OnPropertyChanged(nameof(CtDlpWeek_Body));
        OnPropertyChanged(nameof(CtDlpWeek_Total));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Unscaled));
        OnPropertyChanged(nameof(CtHasDlpWeek));
        OnPropertyChanged(nameof(CtScansPerWeekTotal));

        RaiseCtDlpWeekChanged();
        RecomputeIfCT();
    }


    partial void OnCtDlpHeadPerPatient_mGycm_TextChanged(string value)
    {
        CtDlpHeadPerPatient_mGycm = ParseOrZero(value);
    }

    partial void OnCtDlpBodyPerPatient_mGycmChanged(double value)
    {
        CtDlpBodyPerPatient_mGycm_Text = value.ToString("0.####", CultureInfo.CurrentCulture);
        OnPropertyChanged(nameof(CtDlpBodyPerPatient_mGycm_Text));

        OnPropertyChanged(nameof(CtHeadFractionForDlp));
        OnPropertyChanged(nameof(CtBodyFractionForDlp));
        OnPropertyChanged(nameof(CtAvgDlpPerExam_Base_mGycm));
        OnPropertyChanged(nameof(CtAvgDlpPerExam_Adjusted_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Base_mGycm));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Adjusted_mGycm));

        OnPropertyChanged(nameof(CtDlpWeek_Head));
        OnPropertyChanged(nameof(CtDlpWeek_Body));
        OnPropertyChanged(nameof(CtDlpWeek_Total));
        OnPropertyChanged(nameof(CtDlpWeek_Total_Unscaled));
        OnPropertyChanged(nameof(CtHasDlpWeek));
        OnPropertyChanged(nameof(CtScansPerWeekTotal));

        RaiseCtDlpWeekChanged();
        RecomputeIfCT();
    }

    partial void OnCtDlpBodyPerPatient_mGycm_TextChanged(string value)
    {
        CtDlpBodyPerPatient_mGycm = ParseOrZero(value);
    }


    // partial void OnCtPatientsPerWeekChanged(double value) => RecomputeIfCT();
    partial void OnCtKvpChanged(string value)
    {
        RecomputeIfCT();
    }

    partial void OnCtIsoCenterHeight_ftChanged(double value)
    {
        RecomputeIfCT();
        ApplyCtAutoMap(); // updates SourceToFloor/Ceiling in CT
        RecomputeDistancesForAll();
    }

    partial void OnPatientsPerWeekTextChanged(string value)
    {
        // tolerant parse (current culture first, then invariant)
        if (double.TryParse(value, out var d) ||
            double.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out d))
            PatientsPerWeek = d;
    }

    partial void OnPatientsPerWeekChanged(double value)
    {
        // update the backing field to avoid re-parsing loop…
        PatientsPerWeekText = value.ToString("0.####", CultureInfo.CurrentCulture);
        OnPropertyChanged(nameof(PatientsPerWeekText));
        RecomputeIfCT();
    }



    [ObservableProperty] private string _UserScatterUGyPermAsText = "";
    [ObservableProperty] private string userMasText = "";
   // [ObservableProperty] private string userWMAminPerPatientText = "";
    [ObservableProperty] private string userMaText = "";
    [ObservableProperty] private string userFluoroTimeMinText = "";
    [ObservableProperty] private string userScatterMGyPerHourAt1mText = "";
    [ObservableProperty] private string userDapGyCm2Text = "";
    [ObservableProperty] private string userScatterUGyPerGyCm2Text = "";
    // iso → scatter measurement distance (m), per barrier, edited in CT panel
    [ObservableProperty] private string userIsoToScatterDistanceText = "";
    [ObservableProperty] private double userIsoToScatterDistance_m;


    [ObservableProperty] private double userMasVal;
    [ObservableProperty] private double userScatterUGyPermAsVal;
    //   [ObservableProperty] private double userWMAminPerPatientVal;
    [ObservableProperty] private double userMaVal;
    [ObservableProperty] private double userFluoroTimeMinVal;
    [ObservableProperty] private double userScatterMGyPerHourAt1mVal;
    [ObservableProperty] private double userDapGyCm2Val;
    [ObservableProperty] private double userScatterUGyPerGyCm2Val;

    partial void OnUserIsoToScatterDistanceTextChanged(string value)
    {
        userIsoToScatterDistance_m = ParseOrZero(value);   // value is in meters
        if (SelectedBarrier is not null)
            SelectedBarrier.ScatterIsoToMeasurement_m = userIsoToScatterDistance_m;
        if (IsCT && SelectedBarrier is not null)
            ComputeBarrier(SelectedBarrier);
    }

    partial void OnUserScatterUGyPermAsTextChanged(string value)
    {
        UserScatterUGyPermAsVal = ParseOrZero(value);
        if (SelectedBarrier is not null)
            SelectedBarrier.ScatterUGyPerMasAt1m = UserScatterUGyPermAsVal;

        if (IsCT && SelectedBarrier is not null)
            ComputeBarrier(SelectedBarrier);
    }

    partial void OnUserMasTextChanged(string value)
    {
        UserMasVal = ParseOrZero(value);
    }

    //partial void OnUserWMAminPerPatientTextChanged(string value)
    //{
    //    UserWMAminPerPatientVal = ParseOrZero(value);
    //}

    partial void OnUserMaTextChanged(string value)
    {
        UserMaVal = ParseOrZero(value);
    }

    partial void OnUserFluoroTimeMinTextChanged(string value)
    {
        UserFluoroTimeMinVal = ParseOrZero(value);
    }

    partial void OnUserScatterMGyPerHourAt1mTextChanged(string value)
    {
        UserScatterMGyPerHourAt1mVal = ParseOrZero(value);
        if (SelectedBarrier is not null)
            SelectedBarrier.ScatterRate_mGyPerHourAt1m = UserScatterMGyPerHourAt1mVal;
        if (IsCT && SelectedBarrier is not null)
            ComputeBarrier(SelectedBarrier);
    }

    partial void OnUserDapGyCm2TextChanged(string value)
    {
        UserDapGyCm2Val = ParseOrZero(value);
    }

    partial void OnUserScatterUGyPerGyCm2TextChanged(string value)
    {
        UserScatterUGyPerGyCm2Val = ParseOrZero(value);
        if (SelectedBarrier is not null)
            SelectedBarrier.ScatterUGyPerGyCm2At1m = UserScatterUGyPerGyCm2Val;
        if (IsCT && SelectedBarrier is not null)
            ComputeBarrier(SelectedBarrier);
    }

    // 1) User workload W (mA·min/pt) using cached doubles
    private double ComputeUserW_mAmin()
    {
      //  if (UserWMAminPerPatientVal > 0) return UserWMAminPerPatientVal;
        if (UserMaVal > 0 && UserFluoroTimeMinVal > 0) return UserMaVal * UserFluoroTimeMinVal;
        return 0.0;
    }



    // 3) Scenario lookups
    private bool TryGetScenarioKref(BarrierItemViewModel b, out double kref)
    {
        var scenario = ScenarioFor(b, b.Kind == BarrierKind.Primary);
        if (b.Kind == BarrierKind.Primary) return _data.TryGetPrimaryK1(scenario, out kref);
        return _data.TryGetSecondaryK1(scenario, out kref);
    }

    private bool TryGetScenarioWnorm(BarrierItemViewModel b, out double wnorm)
    {
        var scenario = ScenarioFor(b, b.Kind == BarrierKind.Primary);
        if (b.Kind == BarrierKind.Primary) return _data.TryGetPrimaryWnorm(scenario, out wnorm);
        return _data.TryGetSecondaryWnorm(scenario, out wnorm);
    }

    // 4) Final resolver — keeps UseNcrpK and normalization independent
    private double ResolveK1For(BarrierItemViewModel b, bool logTrace)
    {
        // ───────────────────────────────────────────────────────────────
        // 1) True manual override always wins (only when user actually typed K1)
        // ───────────────────────────────────────────────────────────────
        if (b.K1ManualOverride)
        {
            b.KSource = "Manual";
            return b.K1;
        }

        // ───────────────────────────────────────────────────────────────
        // 2) PET is separate: TG-108 (and only Manual via K1ManualOverride above)
        // ───────────────────────────────────────────────────────────────
        if (IsPET)
        {
            double k = 0.0;
            string src = "";

            bool isSuite = string.Equals(SelectedRoomCategory, "PET/CT Suite (Uptake + PET)", StringComparison.OrdinalIgnoreCase);
            bool uptakeRoomOnly = string.Equals(SelectedRoomCategory, "PET Uptake room", StringComparison.OrdinalIgnoreCase);
            bool petRoomOnly = string.Equals(SelectedRoomCategory, "PET/CT (PET room)", StringComparison.OrdinalIgnoreCase);

            double kPatient = PetPatientDoseConstUsed_uSv_m2_per_MBq_h;
            var kU = PetTg108.K1_Uptake_mGy_perPt_at1m(kPatient, SelectedIsotope, PetAdmin_MBq, PetUptake_min);
            var kP = PetTg108.K1_PetRoom_mGy_perPt_at1m(kPatient, SelectedIsotope, PetAdmin_MBq, PetUptake_min, PetImaging_min, PetVoidingFactor);

            if (uptakeRoomOnly)
            {
                k = kU; src = "TG-108 Uptake";
            }
            else if (petRoomOnly)
            {
                k = kP; src = "TG-108 PET";
            }
            else if (isSuite)
            {
                var reg = b.PetRegion;
                if (reg is PetSourceRegion.Uptake) { k = kU; src = "TG-108 Uptake"; }
                else if (reg is PetSourceRegion.PetRoom) { k = kP; src = "TG-108 PET"; }
                else if (reg is PetSourceRegion.Both)
                {
                    k = kU + kP;
                    src = "TG-108 Uptake+PET";
                }
            }

            if (k > 0)
            {
                b.KSource = src;
                return k;
            }

            b.KSource = "";
            return 0.0;
        }

        // ───────────────────────────────────────────────────────────────
        // Common map inputs, scaled to equivalent @ 1 m using d_meas
        // ───────────────────────────────────────────────────────────────
        double rate_mGyPerHour = b.ScatterRate_mGyPerHourAt1m;
        double mapUGyPerMas = b.ScatterUGyPerMasAt1m;
        double mapUGyPerGyCm2 = b.ScatterUGyPerGyCm2At1m;

        double d_meas = b.ScatterIsoToMeasurement_m;
        if (d_meas <= 0) d_meas = 1.0;

        double scaleTo1m = d_meas * d_meas;

        if (rate_mGyPerHour > 0) rate_mGyPerHour *= scaleTo1m;
        if (mapUGyPerMas > 0) mapUGyPerMas *= scaleTo1m;
        if (mapUGyPerGyCm2 > 0) mapUGyPerGyCm2 *= scaleTo1m;

        // Time and mAs (NOTE: no more mA·min/pt input; time comes only from UserFluoroTimeMinVal)
        double t_min = Math.Max(0.0, UserFluoroTimeMinVal);

        double mAs = UserMasVal > 0
            ? UserMasVal
            : (UserMaVal > 0 && t_min > 0 ? UserMaVal * (t_min * 60.0) : 0.0);

        // ───────────────────────────────────────────────────────────────
        // 3) CT special: DLP+Sutton forces DLP base (if available)
        // ───────────────────────────────────────────────────────────────
        if (IsCT && CtMode == CtCalcMode.DlpPlusSutton)
        {
            if (_data?.CTDlpCoeffs is var (head, body))
            {
                var kHead = CtDlpToK.FromDlp(CtDlpHeadPerPatient_mGycm, head.K1_coeff, head.K_per_cm);
                var kBody = CtDlpToK.FromDlp(CtDlpBodyPerPatient_mGycm, body.K1_coeff, body.K_per_cm);
                var kDlp = kHead + kBody; // mGy/pt @ 1 m

                if (kDlp > 0)
                {
                    b.KSource = "CT DLP";
                    return kDlp;
                }
            }

            // DLP mode chosen but missing inputs/coeffs
            b.KSource = "CT DLP (missing)";
            // fall through to non-DLP precedence below
        }

        // ───────────────────────────────────────────────────────────────
        // 4) NCRP branch (including workload normalization) only when UseNcrpK is ON
        //    (This prevents NCRP "winning" unexpectedly when using CT/fluoro map methods.)
        // ───────────────────────────────────────────────────────────────
        if (b.UseNcrpK && !IsPET)
        {
            if (TryGetScenarioKref(b, out var kref) && kref > 0)
            {
                if (UseNcrpWorkloadNormalization
                    && TryGetScenarioWnorm(b, out var Wnorm) && Wnorm > 0)
                {
                    var Wuser = ComputeUserW_mAmin(); // derived from mA & time now
                    if (Wuser > 0)
                    {
                        b.KSource = "NCRP × W_norm";
                        return kref * (Wuser / Wnorm);
                    }
                }

                b.KSource = "NCRP";
                return kref;
            }

            // NCRP requested but missing data
            b.KSource = "NCRP (missing)";
            return 0.0;
        }

        // ───────────────────────────────────────────────────────────────
        // 5) Deterministic precedence for non-NCRP methods
        //    CT scatter-map: μGy/mAs × mAs (if provided)
        //    Fluoro: Rate × time
        //    DAP: DAP × μGy/(Gy·cm²)
        //    Fallback: NCRP (fallback) if available
        // ───────────────────────────────────────────────────────────────

        // CT/Scatter-map style (works for CT and also any room where map is given)
        if (mapUGyPerMas > 0 && mAs > 0)
        {
            b.KSource = "μGy/mAs*mAs";
            return mapUGyPerMas * mAs / 1000.0;
        }

        // Fluoro / CBCT / angio rate method
        if (rate_mGyPerHour > 0 && t_min > 0)
        {
            b.KSource = "Rate*Time";
            return rate_mGyPerHour * (t_min / 60.0);
        }

        // DAP method
        if (UserDapGyCm2Val > 0 && mapUGyPerGyCm2 > 0)
        {
            b.KSource = "DAP*UGy/GyCm2";
            return UserDapGyCm2Val * mapUGyPerGyCm2 / 1000.0;
        }

        // Fallback NCRP (but DO NOT label as Manual)
        if (TryGetScenarioKref(b, out var krefFallback) && krefFallback > 0)
        {
            b.KSource = "NCRP (fallback)";
            return krefFallback;
        }

        b.KSource = "";
        return 0.0;
    }



    private string ScenarioForRoomPatients()
    {
        return SelectedRoomCategory switch
        {
            "Radiography" => "Rad Room (floor or other barriers)",
            "Dedicated Chest Radiography" => "Chest Room",
            "Rad Tube (R&F room)" => "Rad Tube (R&F room)",
            "Fluoro Tube (R&F room)" => "Fluoro Tube (R&F room)",
            "C-arm" => "C-arm",
            "Mammography" => "Mammography Room",
            "Cardiac Angio" => "Cardiac Angiography",
            "Peripheral Angio" => "Peripheral Angiography",
            "CT" => "CT",
            _ => "Rad Room (floor or other barriers)"
        };
    }

    private void ApplyCategoryExamPresets()
    {
        SurveyExams_per_week = SelectedRoomCategory switch
        {
            "Radiography" => 200,
            "Dedicated Chest Radiography" => 240,
            "Rad Tube (R&F room)" => 180,
            "Mammography" => 200,
            "Fluoro Tube (R&F room)" => 80,
            "C-arm" => 80,
            "Cardiac Angio" => 40,
            "Peripheral Angio" => 30,
            "CT" => 120,
            "PET/CT (PET room)" => 60,
            "PET Uptake room" => 60,
            "PET/CT Suite (Uptake + PET)" => 60,
            _ => SurveyExams_per_week // leave as-is
        };

        if (!IsPatientsNcrpLocked)
            PatientsPerWeek = SelectedRoomCategory switch
            {
                "Radiography" => 60,
                "Dedicated Chest Radiography" => 80,
                "Rad Tube (R&F room)" => 50,
                "Mammography" => 100,
                "Fluoro Tube (R&F room)" => 40,
                "C-arm" => 40,
                "Cardiac Angio" => 30,
                "Peripheral Angio" => 30,
                "CT" => 50,
                "PET/CT (PET room)" => 60,
                "PET Uptake room" => 60,
                "PET/CT Suite (Uptake + PET)" => 60,
                _ => PatientsPerWeek
            };
    }

    public bool IsPatientsNcrpLocked => UseNcrpAverage || UseNcrpBusy;

    private void ApplyNcrpPatientsIfLocked()
    {
        if (IsPET) return;   // NCRP avg/busy should not overwrite PET throughput

        if (!IsPatientsNcrpLocked) return;

        var scenario = ScenarioForRoomPatients();
        if (_data.TryGetPrimaryPatients(scenario, out var nAvgWeek, out var nBusyWeek))
            PatientsPerWeek = UseNcrpBusy ? nBusyWeek : nAvgWeek;
    }


    // Backing checkboxes (assuming you already have the ObservableProperty fields)
    partial void OnUseNcrpAverageChanged(bool value)
    {
        if (value && UseNcrpBusy) UseNcrpBusy = false;
        ApplyNcrpPatientsIfLocked();
        OnPropertyChanged(nameof(IsPatientsNcrpLocked));
    }

    partial void OnUseNcrpBusyChanged(bool value)
    {
        if (value && UseNcrpAverage) UseNcrpAverage = false;
        ApplyNcrpPatientsIfLocked();
        OnPropertyChanged(nameof(IsPatientsNcrpLocked));
    }


    private void OnSelectedBarrierPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not BarrierItemViewModel b) return;

        if (e.PropertyName is nameof(BarrierItemViewModel.EffectiveDistance_m)
            or nameof(BarrierItemViewModel.DistanceOverride_m)
            or nameof(BarrierItemViewModel.X1) or nameof(BarrierItemViewModel.Y1)
            or nameof(BarrierItemViewModel.X2) or nameof(BarrierItemViewModel.Y2))
        {
            OnPropertyChanged(nameof(CtDlpWeek_Head));
            OnPropertyChanged(nameof(CtDlpWeek_Body));
            OnPropertyChanged(nameof(CtDlpWeek_Total));
        }

        if (e.PropertyName is nameof(BarrierItemViewModel.ApplyPreShield)
            or nameof(BarrierItemViewModel.PreShieldType)
            or nameof(BarrierItemViewModel.MaterialName)
            or nameof(BarrierItemViewModel.Designation)
            or nameof(BarrierItemViewModel.X1)
            or nameof(BarrierItemViewModel.Y1)
            or nameof(BarrierItemViewModel.X2)
            or nameof(BarrierItemViewModel.Y2)
            or nameof(BarrierItemViewModel.UseNcrpK))
            AutoFillBarrierFromTables(b);

        if (e.PropertyName == nameof(BarrierItemViewModel.DistanceOverride_m))
            // Snap only when there is a positive override and the barrier is a wall-like drawable
            if (b.DistanceOverride_m > 0 && b.IsDrawable && !GetRuleDistance_m(b).HasValue && b.SnapToSource)
            {
                SnapBarrierToRawDistance(b, b.DistanceOverride_m);
                // After snapping, keep distances in sync for everything (optional but safe)
                RecomputeDistancesForAll();
                UpdateCanvasExtent();
            }

        if (!CtHasDlpWeek && b.IsCtTertiary) // NEW guard
            b.IsCtTertiary = false;

        if (e.PropertyName is nameof(BarrierItemViewModel.IsCtTertiary)
            or nameof(BarrierItemViewModel.TertiaryHeight_m)
            or nameof(BarrierItemViewModel.TertiaryHeightText))
            if (IsCT)
                ComputeBarrier(b);

        if (e.PropertyName is nameof(BarrierItemViewModel.Zone)
        or nameof(BarrierItemViewModel.PMGyWeek)
        or nameof(BarrierItemViewModel.UseFactor)
        or nameof(BarrierItemViewModel.Occupancy)
        or nameof(BarrierItemViewModel.MaterialName)
        or nameof(BarrierItemViewModel.ApplyPreShield)
        or nameof(BarrierItemViewModel.PreShieldType)
        or nameof(BarrierItemViewModel.PreShield_mm)
        or nameof(BarrierItemViewModel.K1)
        or nameof(BarrierItemViewModel.K1Text)
        or nameof(BarrierItemViewModel.K1ManualOverride)
        or nameof(BarrierItemViewModel.UseNcrpK)
        or nameof(BarrierItemViewModel.DistanceOverride_m)
        or nameof(BarrierItemViewModel.SaveDistance)
        or nameof(BarrierItemViewModel.SnapToSource)
        or nameof(BarrierItemViewModel.Designation)
        or nameof(BarrierItemViewModel.IsCtTertiary)
        or nameof(BarrierItemViewModel.TertiaryHeight_m)
        or nameof(BarrierItemViewModel.TertiaryHeightText))
        {
            ComputeBarrier(b);
        }
    }

    // Remember previous selection to support pairwise actions (e.g., Set Gap)
    private BarrierItemViewModel? _lastSelectedBarrier;

    // ----------------------- Gap helpers -----------------------
    private static void UnitNormalAndMid(BarrierItemViewModel b, out double nx, out double ny,
        out double mx, out double my,
        out double ux, out double uy)
    {
        double dx = b.X2 - b.X1, dy = b.Y2 - b.Y1;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9)
        {
            nx = ny = mx = my = ux = uy = 0;
            return;
        }

        ux = dx / len;
        uy = dy / len;
        nx = -uy;
        ny = ux;
        mx = (b.X1 + b.X2) * 0.5;
        my = (b.Y1 + b.Y2) * 0.5;
    }

    private void OnSetGap(object? parameter)
    {
        if (parameter is null) return;

        double desired;

        if (parameter is double d)
        {
            desired = d;
        }
        else if (parameter is string s)
        {
            if (!double.TryParse(s, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out desired) &&
                !double.TryParse(s, NumberStyles.Float,
                    CultureInfo.CurrentCulture, out desired))
                return; // parse failed
        }
        else
        {
            return; // unsupported type
        }

        if (desired <= 0) return;
        var meters = FtToM(desired);
        SetGapBetweenSelectedAndPrevious(meters);
    }


    private void SetGapBetweenSelectedAndPrevious(double desiredGap_m)
    {
        var a = SelectedBarrier;
        var b = _lastSelectedBarrier;
        if (a is null || b is null) return;
        if (!a.IsDrawable || !b.IsDrawable) return;
        if (desiredGap_m <= 0) return;

        // Respect rule-driven distances (don’t move if rule applies)
        if (GetRuleDistance_m(a).HasValue || GetRuleDistance_m(b).HasValue) return;

        // Compute normals/tangents and verify parallelism
        UnitNormalAndMid(a, out var nax, out var nay, out var max, out var may, out var uax, out var uay);
        UnitNormalAndMid(b, out var nbx, out var nby, out var mbx, out var mby, out var ubx, out var uby);
        var parallel = Math.Abs(uax * ubx + uay * uby);
        if (parallel < 0.95) return; // not parallel enough

        // Vector from B→A in pixels and signed gap along B's normal
        double vx = max - mbx, vy = may - mby;
        var gap_px = vx * nbx + vy * nby;
        var pxToMeters = CmPerPixel / 100.0;
        var gap_m = gap_px * pxToMeters;

        // Keep current side (sign), change magnitude to desiredGap_m
        var target_gap_m = Math.Sign(gap_m == 0 ? 1 : gap_m) * desiredGap_m;
        var delta_m = target_gap_m - gap_m;
        if (Math.Abs(delta_m) < 1e-6) return;

        var shift_px = delta_m / pxToMeters;
        var sx = shift_px * nbx;
        var sy = shift_px * nby;

        // Clamp so both endpoints stay within canvas bounds [0..CanvasWidth/Height]
        double W = CanvasWidth, H = CanvasHeight;

        // We’ll solve for a scalar t (in pixels) we’re allowed to apply along (nx, ny).
        // For each endpoint, ensure: 0 <= (Xi + nx*t) <= W and 0 <= (Yi + ny*t) <= H
        (double minT, double maxT) = (-1e12, 1e12);
        void IntersectInterval(double coord, double dir, double min, double max)
        {
            // coord + dir * t in [min, max]
            if (Math.Abs(dir) < 1e-9)
            {
                // If dir ~ 0, just ensure coord is already in-bounds; otherwise no feasible t
                if (coord < min || coord > max)
                {
                    // no possible t keeps this in-bounds; collapse interval to 0 shift
                    minT = 0; maxT = 0;
                }
                return;
            }
            var t1 = (min - coord) / dir;
            var t2 = (max - coord) / dir;
            var lo = Math.Min(t1, t2);
            var hi = Math.Max(t1, t2);
            if (lo > minT) minT = lo;
            if (hi < maxT) maxT = hi;
        }

        // Current endpoints
        IntersectInterval(a.X1, nbx, 0, W);
        IntersectInterval(a.Y1, nby, 0, H);
        IntersectInterval(a.X2, nbx, 0, W);
        IntersectInterval(a.Y2, nby, 0, H);

        // We want to apply t = shift_px, but clamp to [minT, maxT]
        var tAllowed = Math.Max(minT, Math.Min(shift_px, maxT));

        // Apply clamped shift
        sx = tAllowed * nbx;
        sy = tAllowed * nby;

        a.X1 += sx; a.Y1 += sy;
        a.X2 += sx; a.Y2 += sy;

        RecomputeDistancesForAll();
    }

    private static string F4(double v) =>
    v == 0 ? "" : v.ToString("0.####", CultureInfo.CurrentCulture);

    partial void OnSelectedBarrierChanged(BarrierItemViewModel? oldValue, BarrierItemViewModel? newValue)
    {
        if (ReferenceEquals(oldValue, newValue)) return;

        if (_subscribedBarrier is not null)
            _subscribedBarrier.PropertyChanged -= OnSelectedBarrierPropertyChanged;

        _subscribedBarrier = newValue;

        if (oldValue is not null) oldValue.IsSelected = false;

        if (newValue is not null)
        {
            newValue.IsSelected = true;

            if (newValue.Kind == BarrierKind.Secondary && newValue.ApplyPreShield)
                newValue.ApplyPreShield = false;
        }

        if (_subscribedBarrier is not null)
            _subscribedBarrier.PropertyChanged += OnSelectedBarrierPropertyChanged;

        ShowCachedTrace(newValue);

        if (oldValue is not null) _lastSelectedBarrier = oldValue;

        ApplyNcrpPatientsIfLocked();

        if (newValue is not null)
        {
            UserScatterMGyPerHourAt1mText = F4(newValue.ScatterRate_mGyPerHourAt1m);
            UserScatterUGyPermAsText = F4(newValue.ScatterUGyPerMasAt1m);
            UserScatterUGyPerGyCm2Text = F4(newValue.ScatterUGyPerGyCm2At1m);
            UserIsoToScatterDistanceText = F4(newValue.ScatterIsoToMeasurement_m);
        }
        else
        {
            UserIsoToScatterDistanceText = "";
            userIsoToScatterDistance_m = 0;
        }

        OnPropertyChanged(nameof(CtDlpWeek_Head));
        OnPropertyChanged(nameof(CtDlpWeek_Body));
        OnPropertyChanged(nameof(CtDlpWeek_Total));
    }

    private static double ParseOrZero(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0.0;
        var t = s.Trim();

        // Try current culture first (supports comma decimals if that’s your locale)
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out var v))
            return v;

        // Fallback: invariant culture with dot decimals
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            return v;

        return 0.0;
    }

    // Use the generated properties (ConvFeet, ConvInches) – not the fields.
    public double ConvFeetOnly
        => ParseOrZero(ConvFeetText) + ParseOrZero(ConvInchesText) / 12.0;

    // ft → m
    public double ConvFeetToMeters => ConvFeetOnly * 0.3048;

    // ft → in
    public double ConvFeetToInches => ConvFeetOnly * 12.0;

    partial void OnConvFeetTextChanged(string value)
    {
        OnPropertyChanged(nameof(ConvFeetOnly));
        OnPropertyChanged(nameof(ConvFeetToMeters));
        OnPropertyChanged(nameof(ConvFeetToInches));
    }

    partial void OnConvInchesTextChanged(string value)
    {
        OnPropertyChanged(nameof(ConvFeetOnly));
        OnPropertyChanged(nameof(ConvFeetToMeters));
        OnPropertyChanged(nameof(ConvFeetToInches));
    }

    // m → ft/in (writes into the existing feet+inches fields)
    partial void OnConvMetersTextChanged(string value)
    {
        var m = ParseOrZero(value);
        // total feet from meters
        var totalFeet = m / 0.3048;
        var feetWhole = Math.Truncate(totalFeet);
        var inches = (totalFeet - feetWhole) * 12.0;

        ConvFeetText = feetWhole.ToString("0.####", CultureInfo.CurrentCulture);
        ConvInchesText = inches.ToString("0.####", CultureInfo.CurrentCulture);
        // ConvFeetOnly / ConvFeetToMeters / ConvFeetToInches all refresh via the setters above
    }


    // Use NCRP normalization (scale K1 by W / W_norm)
    [ObservableProperty] private bool useNcrpWorkloadNormalization = false;

    // Use NCRP K1 when no user dose:
    [ObservableProperty] private bool preferNcrpK1WhenEmpty = true; // default to true


    // ===== Right panel (summary + trace) =====
    [ObservableProperty] private string resultSummary = "—";
    public ObservableCollection<KeyValuePair<string, string>> TraceItems { get; } = new();

    // ===== Commands =====
    public IRelayCommand AddPrimaryCommand { get; }
    public IRelayCommand AddSecondaryCommand { get; }
    public IRelayCommand RemoveSelectedCommand { get; }
    public IRelayCommand ComputeSelectedCommand { get; }
    public IRelayCommand ComputeAllCommand { get; }
    public IRelayCommand StartMeasureScaleCommand { get; }

    public IRelayCommand
        StartMeasureDistanceCommand
    { get; } // = new RelayCommand(() => IsMeasuringDistance = true);

    partial void OnRoomHeight_ftChanged(double value)
    {
        ApplyCtAutoMap();
        RecomputeDistancesForAll();
    }

    partial void OnLevelBelowHeight_ftChanged(double value)
    {
        RecomputeDistancesForAll();
    }


    partial void OnTubeXChanged(double value)
    {
        RecomputeDistancesForAll();
    }

    partial void OnTubeYChanged(double value)
    {
        RecomputeDistancesForAll();
    }

    partial void OnCmPerPixelChanged(double value)
    {
        RecomputeDistancesForAll();
    }

    public void RecomputeDistancesForAll()
    {
        foreach (var b in Barriers)
        {
            // 1) Base distance (NO offsets)
            double base_m;

            if (b.DistanceOverride_m > 0)
            {
                base_m = b.DistanceOverride_m; // user override is the base
            }
            else
            {
                var rule = GetRuleDistance_m(b); // Floor/Ceiling
                if (rule.HasValue)
                {
                    base_m = rule.Value;
                }
                else if (b.SaveDistance && b.SavedRawDistance_m > 0)
                {
                    base_m = b.SavedRawDistance_m;
                }
                else
                {
                    base_m = ComputeCanvasDistance_m(b);
                }
            }

            b.DisplayDistance_m = base_m; // what UI shows as "Distance from source"
            b.AutoDistance_m = base_m;    // store base only (no offsets)

            b.RaiseEffectiveDistanceChanged();
        }
    }



    // -----------------------  Layout helpers  -----------------------
    private static bool IsTypicallySecondaryRoom(string? room)
    {
        if (string.IsNullOrWhiteSpace(room)) return false;

        return room is "Fluoro Tube (R&F room)" or "C-arm" or "Mammography" or "CT"
            or "Cardiac Angio" or "Peripheral Angio" or "PET/CT (PET room)" or "PET Uptake room" or "PET/CT Suite (Uptake + PET)";
    }

    private static string BareBarrierTitle(BarrierItemViewModel b)
    {
        return $"{(b.Kind == BarrierKind.Primary ? "Primary" : "Secondary")} - {b.Designation}";
    }


    private void LoadDefaultLayoutForRoom()
    {
        Barriers.Clear();

        // Common canvas defaults
        if (IsLoadingProject) return;
        SetDefaultCanvas(560, 360, 0.5); // ~0.25 cm/px → coarse, adjust as you like
        UseNcrpAverage = false;
        UseNcrpBusy = false;
        UseNcrpWorkloadNormalization = false;
        // local helper to add a wall quickly
        BarrierItemViewModel AddWall(
            string name, BarrierKind kind, BarrierDesignation desig,
            double x1, double y1, double x2, double y2,
            string mat = "Lead", double U = 1.0, double T = 1.0,
            string zone = "Uncontrolled", // keep zone
            bool preShield = false, string? preType = null, string area = "Adjacent")
        {
            var b = new BarrierItemViewModel(this)
            {
                Name = name,
                Kind = kind,
                Designation = desig,
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                MaterialName = mat,
                UseFactor = U,
                Occupancy = T,
                Zone = zone, // <- drives P via OnZoneChanged (with suppression)
                ApplyPreShield = preShield,
                PreShieldType = preType ?? "",
                ProtectedArea = area
            };
            b.UseNcrpK = !IsPET && !(kind == BarrierKind.Primary && IsTypicallySecondaryRoom(SelectedRoomCategory!));
            b.InitDisplayName();
            b.RefreshLabelPosition();
            Barriers.Add(b);
            b.GetCanvasDistance_m = ComputeCanvasDistance_m;

            AutoFillBarrierFromTables(b);
            UpdateCanvasExtent();
            return b;
        }


        var cat = SelectedRoomCategory ?? "Radiography";

        if (cat == "Radiography")
        {
            // (kept your original Radiography defaults)
            var primaryChest = AddWall("Primary – Chest bucky wall", BarrierKind.Primary,
                BarrierDesignation.ChestBuckyWall,
                320, 220, 700, 220, U: 1.0, T: 1.0, zone: "Uncontrolled", preShield: true,
                preType: "Table/Wall bucky receptor", area: "Corridor");
            var primaryLeft = AddWall("Primary – Cross Lat. left", BarrierKind.Primary,
                BarrierDesignation.CrossTableLateralWall,
                320, 220, 320, 680, U: 0.09, T: 1.0, zone: "Controlled", preShield: true,
                preType: "Cross lateral Grid/cassette", area: "Control room");
            var primaryRight = AddWall("Primary – Cross Lat. right", BarrierKind.Primary,
                BarrierDesignation.CrossTableLateralWall,
                700, 220, 700, 680, U: 0.09, T: 1.0, zone: "Uncontrolled", preShield: true,
                preType: "Cross lateral Grid/cassette", area: "Corridor");
            var secondaryBot = AddWall("Secondary – Other wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                320, 600, 700, 600, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Offices");

            SelectedBarrier = primaryChest;
        }
        else if (cat == "Rad Tube (R&F room)" || cat == "Dedicated Chest Radiography")
        {
            // Radiography-like: include a primary chest bucky and two cross-table primaries
            var primaryChest = AddWall("Primary – Chest bucky wall", BarrierKind.Primary,
                BarrierDesignation.ChestBuckyWall,
                320, 220, 700, 220, U: 1.0, T: 1.0, zone: "Uncontrolled", preShield: true,
                preType: "Table/Wall bucky receptor", area: "Corridor");
            var primaryLeft = AddWall("Primary – Cross Lat. left", BarrierKind.Primary,
                BarrierDesignation.CrossTableLateralWall,
                320, 220, 320, 680, U: 0.09, T: 1.0, zone: "Controlled", preShield: true,
                preType: "Cross lateral Grid/cassette", area: "Control room");
            var primaryRight = AddWall("Primary – Cross Lat. right", BarrierKind.Primary,
                BarrierDesignation.CrossTableLateralWall,
                700, 220, 700, 680, U: 0.09, T: 1.0, zone: "Uncontrolled", preShield: true,
                preType: "Cross lateral Grid/cassette", area: "Corridor");
            var secondaryBot = AddWall("Secondary – Other wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                320, 600, 700, 600, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Offices");
            SelectedBarrier = primaryChest;
        }
        else if (cat == "Fluoro Tube (R&F room)")
        {
            // Fluoro: secondary only
            var top = AddWall("Secondary – Top wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260, 180, 860,
                180, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Corridor");
            var left = AddWall("Secondary – Left wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260, 180,
                260, 640, U: 1.0, T: 1.0, zone: "Controlled", area: "Control room");
            var right = AddWall("Secondary – Right wall", BarrierKind.Secondary, BarrierDesignation.Wall, 860, 180,
                860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Office");
            var bottom = AddWall("Secondary – Bottom wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260,
                640, 860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Hallway");
            SelectedBarrier = top;
        }
        else if (cat == "Cardiac Angio")
        {
            // Cardiac: secondary only; usually controlled adjacent spaces
            var top = AddWall("Secondary – Top wall (controlled)", BarrierKind.Secondary, BarrierDesignation.Wall,
                260, 180, 860, 180, U: 1.0, T: 1.0, zone: "Controlled", area: "Control room");
            var left = AddWall("Secondary – Left wall (controlled)", BarrierKind.Secondary, BarrierDesignation.Wall,
                260, 180, 260, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Equipment room");
            var right = AddWall("Secondary – Right wall", BarrierKind.Secondary, BarrierDesignation.Wall, 860, 180,
                860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Corridor");
            var bottom = AddWall("Secondary – Bottom wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260,
                640, 860, 640, U: 1.0, T: 1.0, zone: "Controlled", area: "Work area");
            SelectedBarrier = top;
        }
        else if (cat == "Peripheral Angio")
        {
            // Peripheral Angio: similar to cardiac
            var top = AddWall("Secondary – Top wall (controlled)", BarrierKind.Secondary, BarrierDesignation.Wall,
                260, 180, 860, 180, U: 1.0, T: 1.0, zone: "Controlled", area: "Control room");
            var left = AddWall("Secondary – Left wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260, 180,
                260, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Equipment");
            var right = AddWall("Secondary – Right wall", BarrierKind.Secondary, BarrierDesignation.Wall, 860, 180,
                860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Corridor");
            var bottom = AddWall("Secondary – Bottom wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260,
                640, 860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Office");
            SelectedBarrier = top;
        }
        else if (cat == "Mammography")
        {
            // Mammo: secondary to walls; set low P for public corridors by default
            var top = AddWall("Secondary – Top wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260, 180, 860,
                180, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Corridor");
            var left = AddWall("Secondary – Left wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260, 180,
                260, 640, U: 1.0, T: 1.0, zone: "Controlled", area: "Exam room");
            var right = AddWall("Secondary – Right wall", BarrierKind.Secondary, BarrierDesignation.Wall, 860, 180,
                860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Office");
            var bottom = AddWall("Secondary – Bottom wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260,
                640, 860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Hallway");
            SelectedBarrier = top;
        }
        else if (cat == "CT")
        {
            // ApplyCtDefaults();
            // CT: secondary walls
            var top = AddWall("Secondary – Top wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260, 180, 860,
                180, U: 1.0, T: 1.0, zone: "Controlled", area: "Control room");
            var left = AddWall("Secondary – Left wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260, 180,
                260, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Equipment room");
            var right = AddWall("Secondary – Right wall", BarrierKind.Secondary, BarrierDesignation.Wall, 860, 180,
                860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Corridor");
            var bottom = AddWall("Secondary – Bottom wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260,
                640, 860, 640, U: 1.0, T: 1.0, zone: "Controlled", area: "Exam room");
            SelectedBarrier = top;
        }
        else if (cat == "PET Uptake room")
        {
            // Uptake: secondary-only walls (staff/public outside)
            var top = AddWall("Secondary – Top wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                260, 180, 860, 180, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Corridor");
            var left = AddWall("Secondary – Left wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                260, 180, 260, 640, U: 1.0, T: 1.0, zone: "Controlled", area: "Control room");
            var right = AddWall("Secondary – Right wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                860, 180, 860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Office");
            var bottom = AddWall("Secondary – Bottom wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                260, 640, 860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Hallway");

            SelectedBarrier = top;
        }
        else if (cat == "PET/CT (PET room)")
        {
            // PET/CT room: secondary-only walls (patient source during imaging)
            var top = AddWall("Secondary – Top wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                260, 180, 860, 180, U: 1.0, T: 1.0, zone: "Controlled", area: "Control room");
            var left = AddWall("Secondary – Left wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                260, 180, 260, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Equipment room");
            var right = AddWall("Secondary – Right wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                860, 180, 860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Corridor");
            var bottom = AddWall("Secondary – Bottom wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                260, 640, 860, 640, U: 1.0, T: 1.0, zone: "Controlled", area: "Exam room");

            SelectedBarrier = top;
        }
        else if (cat == "PET/CT Suite (Uptake + PET)")
        {
            var top = AddWall("Secondary – Top wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                260, 180, 860, 180, U: 1.0, T: 1.0, zone: "Controlled", area: "Control room");

            var left = AddWall("Secondary – Left wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                260, 180, 260, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Corridor");

            var right = AddWall("Secondary – Right wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                860, 180, 860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Corridor");

            var bottom = AddWall("Secondary – Bottom wall", BarrierKind.Secondary, BarrierDesignation.Wall,
                260, 640, 860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Office");

            // optional defaults
            top.PetRegion = PetSourceRegion.PetRoom;
            bottom.PetRegion = PetSourceRegion.Uptake;

            SelectedBarrier = top;
        }
        else
        {
            // Fallback: create a simple 4-wall secondary room so it's never blank
            var top = AddWall("Secondary – Top wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260, 180, 860,
                180, U: 1.0, T: 1.0, zone: "Controlled", area: "Control room");
            var left = AddWall("Secondary – Left wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260, 180,
                260, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Equipment room");
            var right = AddWall("Secondary – Right wall", BarrierKind.Secondary, BarrierDesignation.Wall, 860, 180,
                860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Corridor");
            var bottom = AddWall("Secondary – Bottom wall", BarrierKind.Secondary, BarrierDesignation.Wall, 260,
                640, 860, 640, U: 1.0, T: 1.0, zone: "Uncontrolled", area: "Office");
            SelectedBarrier = top;
        }


        // Finalize: compute defaults and auto-fill tables for all barriers
        foreach (var b in Barriers)
        {
            b.Label = string.IsNullOrWhiteSpace(b.Label) ? "" : b.Label;
            if (b.Kind == BarrierKind.Primary && IsTypicallySecondaryRoom(SelectedRoomCategory!))
                b.UseNcrpK = false;
            // else leave as set by AddWall/AddPrimaryBarrier
            AutoFillBarrierFromTables(b);
        }

        SelectedBarrier ??= Barriers.FirstOrDefault();
        ResultSummary = SelectedBarrier is null ? "—" : $"{BareBarrierTitle(SelectedBarrier)}: ready";
        RecomputeDistancesForAll();
    }


    public double ComputeCanvasDistance_m(BarrierItemViewModel b)
    {
        var pxToMeters = CmPerPixel / 100.0;
        var dPixels = DistancePointToSegment(TubeX, TubeY, b.X1, b.Y1, b.X2, b.Y2);
        return dPixels * pxToMeters; // RAW, no offsets
    }

    // MainViewModel.cs
    private void SnapBarrierToRawDistance(BarrierItemViewModel b, double targetDistance_m)
    {
        if (b is null || !b.IsDrawable || GetRuleDistance_m(b).HasValue) return;
        if (!(targetDistance_m > 0)) return;

        // Geometry in canvas px
        double dx = b.X2 - b.X1, dy = b.Y2 - b.Y1;
        double L = Math.Sqrt(dx * dx + dy * dy);
        if (L < 1e-6) return;

        // Unit tangent/normal
        double ux = dx / L, uy = dy / L;
        double nx = -uy, ny = ux;

        // Vector from P1 to the tube
        double wx = TubeX - b.X1, wy = TubeY - b.Y1;

        // Decompose into tangent/normal comps
        double s = wx * ux + wy * uy;        // signed arclength along segment (px)
        double n = wx * nx + wy * ny;        // signed normal distance to the *line* (px)

        // Clamp s to segment -> residual parallel component to the nearest point on the segment
        double sClamped = Math.Max(0, Math.Min(L, s));
        double p = s - sClamped;             // parallel "overshoot" in px (0 if within segment)

        // Current point-to-segment distance (px)
        // double cur_px = Math.Sqrt(n * n + p * p);

        // Target in px
        double metersPerPixel = CmPerPixel / 100.0;
        double target_px = targetDistance_m / metersPerPixel;

        // If p^2 > target^2, we cannot reach the target by sliding only along the normal
        // (it would require moving tangentially). In that case, snap as close as possible.
        double rad2 = target_px * target_px - p * p;
        if (rad2 < 0) rad2 = 0;

        // Preserve which side of the wall the tube is on
        double side = Math.Sign(n);
        if (side == 0) side = 1;

        // We want sqrt((n - t)^2 + p^2) == target  =>  (n - t) = side * sqrt(target^2 - p^2)
        double n_target = side * Math.Sqrt(rad2);
        double shift_px = n - n_target;      // shift along +n

        // Clamp shift so endpoints remain on-canvas
        double W = CanvasWidth, H = CanvasHeight;
        (double minT, double maxT) = (-1e12, 1e12);
        void IntersectInterval(double coord, double dir, double min, double max)
        {
            if (Math.Abs(dir) < 1e-9) { if (coord < min || coord > max) { minT = maxT = 0; } return; }
            double t1 = (min - coord) / dir, t2 = (max - coord) / dir;
            double lo = Math.Min(t1, t2), hi = Math.Max(t1, t2);
            if (lo > minT) minT = lo; if (hi < maxT) maxT = hi;
        }
        IntersectInterval(b.X1, nx, 0, W); IntersectInterval(b.Y1, ny, 0, H);
        IntersectInterval(b.X2, nx, 0, W); IntersectInterval(b.Y2, ny, 0, H);

        double tAllowed = Math.Max(minT, Math.Min(shift_px, maxT));
        double sx = tAllowed * nx, sy = tAllowed * ny;

        b.X1 += sx; b.Y1 += sy;
        b.X2 += sx; b.Y2 += sy;

        // Refresh displayed/auto distances (segment-based raw; walls keep offset)
        var raw = ComputeCanvasDistance_m(b);
        b.AutoDistance_m = Math.Max(0, raw); // base only
        b.DisplayDistance_m = raw;
        b.RaiseEffectiveDistanceChanged();
    }


    public void ResnapSelectedBarrierToOverride()
    {
        var b = SelectedBarrier;
        if (b is null) return;

        // Only for drawable walls (not floor/ceiling rules)
        var rule = GetRuleDistance_m(b);
        if (!b.IsDrawable || rule.HasValue) return;

        var t = b.DistanceOverride_m; // meters; 0 means "use drawing"
        if (t <= 0) return;
        if (b.SnapToSource)
            SnapBarrierToRawDistance(b, t);
        RecomputeDistancesForAll();
        UpdateCanvasExtent();
    }


    private double FtToM(double ft)
    {
        return ft * 0.3048;
    }

    public double? GetRuleDistance_m(BarrierItemViewModel b)
    {
        if (b is null) return null;

        if (b.Designation == BarrierDesignation.Floor)
        {
            // pure source→floor (meters), no downstairs offset here
            return FtToM(SourceToFloor_ft);
        }

        if (b.Designation == BarrierDesignation.Ceiling)
        {
            // pure source→ceiling (meters), no +0.5 here
            return FtToM(SourceToCeiling_ft);
        }

        return null; // walls/doors/windows use canvas raw
    }



    private static double DistancePointToSegment(double px, double py,
        double x1, double y1, double x2, double y2)
    {
        double vx = x2 - x1, vy = y2 - y1;
        double wx = px - x1, wy = py - y1;
        var c1 = vx * wx + vy * wy;
        if (c1 <= 0) return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));
        var c2 = vx * vx + vy * vy;
        if (c2 <= c1) return Math.Sqrt((px - x2) * (px - x2) + (py - y2) * (py - y2));
        var t = c1 / c2;
        double projx = x1 + t * vx, projy = y1 + t * vy;
        return Math.Sqrt((px - projx) * (px - projx) + (py - projy) * (py - projy));
    }


    private string ScenarioFor(BarrierItemViewModel b, bool primary)
    {
        // Map UI categories to your bundle scenario keys
        var cat = SelectedRoomCategory ?? "Radiography";

        if (cat == "Radiography")
        {
            if (primary)
                return b.Designation == BarrierDesignation.ChestBuckyWall
                    ? "Rad Room (chest bucky)"
                    : "Rad Room (floor or other barriers)";

            if (b.Designation == BarrierDesignation.Floor) return "Rad Room (floor or other barriers)";
            if (b.Designation == BarrierDesignation.ChestBuckyWall) return "Rad Room (chest bucky)";
            return "Rad Room (all barriers)";
        }

        if (cat == "Dedicated Chest Radiography")
        {
            if (primary)
                return b.Designation == BarrierDesignation.ChestBuckyWall
                    ? "Chest Room"
                    : "Rad Room (floor or other barriers)";

            if (b.Designation == BarrierDesignation.Floor) return "Rad Room (floor or other barriers)";
            if (b.Designation == BarrierDesignation.ChestBuckyWall) return "Chest Room";
            return "Chest Room";
        }

        if (cat == "Rad Tube (R&F room)")
        {
            if (primary)
                return b.Designation == BarrierDesignation.ChestBuckyWall
                    ? "Rad Room (chest bucky)"
                    : "Rad Tube (R&F room)";

            if (b.Designation == BarrierDesignation.Floor) return "Rad Tube (R&F room)";
            if (b.Designation == BarrierDesignation.ChestBuckyWall) return "Rad Room (chest bucky)";
            return "Rad Tube (R&F room)";
        }

        // Fluoroscopy / Angio: no primary to walls; always secondary/leakage/scatter
        if (cat == "Fluoro Tube (R&F room)")
        {
            if (primary)
                return b.Designation == BarrierDesignation.ChestBuckyWall
                    ? "Rad Room (chest bucky)"
                    : "Fluoro Tube (R&F room)";

            if (b.Designation == BarrierDesignation.Floor) return "Fluoro Tube (R&F room)";
            if (b.Designation == BarrierDesignation.ChestBuckyWall) return "Rad Room (chest bucky)";
            return "Fluoro Tube (R&F room)";
        }
        

        if (cat == "Cardiac Angio")
            return "Cardiac Angiography";

        if (cat == "Peripheral Angio")
            return "Peripheral Angiography";

        if (cat == "Mammography")
            return "Mammography Room";

        if (cat == "CT")
            return "CT";

        if (cat == "C-arm")
            return "C-arm";

        // Fallback
        return "Rad Room (all barriers)";
    }

    private void SetDefaultCanvas(double tubeXpx, double tubeYpx, double cmPerPixelVal)
    {
        TubeX = tubeXpx;
        TubeY = tubeYpx;
        CmPerPixel = cmPerPixelVal > 0 ? cmPerPixelVal : 0.5; // avoid 0 scale
    }

    private void AutoFillBarrierFromTables(BarrierItemViewModel b)
    {
        // Use factor from Table 4.4 (primary only)
        if (b.Kind == BarrierKind.Primary)
        {
            var desig = b.Designation switch
            {
                BarrierDesignation.ChestBuckyWall => "Chest Bucky Wall",
                BarrierDesignation.Floor => "Floor",
                BarrierDesignation.CrossTableLateralWall => "Cross-Table Lateral Wall",
                BarrierDesignation.Wall => "Other Walls",
                _ => null
            };
            if (desig != null && _data.UseFactors.TryGetValue(desig, out var uf))
                b.UseFactor = uf.U;
        }

        if (b.Kind == BarrierKind.Secondary) b.ApplyPreShield = false;

        // K1 defaults (primary/secondary)
        var scK = ScenarioFor(b, b.Kind == BarrierKind.Primary);

        if (b.UseNcrpK)
        {
            var haveK = b.Kind == BarrierKind.Primary
                ? _data.TryGetPrimaryK1(scK, out var kref)
                : _data.TryGetSecondaryK1(scK, out kref);

            if (haveK)
            {
                if (UseNcrpWorkloadNormalization
                    && TryGetScenarioWnorm(b, out var Wnorm) && Wnorm > 0)
                {
                    var Wuser = ComputeUserW_mAmin();
                    var k = Wuser > 0 ? kref * (Wuser / Wnorm) : kref;
                    b.SetK1FromEngine(k);
                    // b.K1 = Wuser > 0 ? kref * (Wuser / Wnorm) : kref;
                    b.KSource = Wuser > 0 ? "W_norm" : "NCRP";
                }
                else
                {
                    b.SetK1FromEngine(kref);
                    b.KSource = "NCRP";
                }
            }
        }


        // Pre-shield x_pre (primary only)
        if (b.Kind == BarrierKind.Primary && b.ApplyPreShield && !string.IsNullOrWhiteSpace(b.PreShieldType))
        {
            if (_data.PrimaryPreShield.TryGetValue(b.PreShieldType, out var spec))
            {
                var m = (b.MaterialName ?? "").ToLowerInvariant();
                b.PreShield_mm = m.Contains("lead") ? spec.Lead_mm :
                    m.Contains("concrete") ? spec.Concrete_mm :
                    m.Contains("steel") ? spec.Steel_mm : 0.0;
            }
            else
            {
                b.PreShield_mm = 0.0;
            }
        }
        else
        {
            b.PreShield_mm = 0.0;
        }

        // Distance from drawing (with offsets)
        RecomputeDistancesForAll();
    }

    // -----------------------  Add / remove helpers  -----------------------

    private void AddPrimaryBarrier()
    {
        //var defP = SelectedZone == "Controlled" ? 0.1 : 0.02;
        var b = new BarrierItemViewModel(this)
        {
            Label = SuggestLabel(),
            Name = "",
            Kind = BarrierKind.Primary,
            Designation = BarrierDesignation.ChestBuckyWall,
            X1 = 400,
            Y1 = 200,
            X2 = 700,
            Y2 = 200,
            MaterialName = "Lead", //MaterialNames.FirstOrDefault() ?? "Lead",
            UseFactor = 1.0,
            Occupancy = 1.0,
            Zone = "Uncontrolled",
            //PMGyWeek = 0.02,
            POverridden = false,
            N = 1,
            ApplyPreShield = true,
            PreShieldType = "Table/Wall bucky receptor",
            ProtectedArea = "Corridor"
         };


        //if (!b.POverridden) b.PMGyWeek = defP2;

        b.InitDisplayName();
        b.RefreshLabelPosition();
        Barriers.Add(b);
        b.GetCanvasDistance_m = ComputeCanvasDistance_m;
        SelectedBarrier = b;
        b.UseNcrpK = !IsPET && !IsTypicallySecondaryRoom(SelectedRoomCategory); // allow manual/map by default in those rooms
        AutoFillBarrierFromTables(b);
        UpdateCanvasExtent();
    }

    private void AddSecondaryBarrier()
    {
        //var defP = SelectedZone == "Controlled" ? 0.1 : 0.02;
        var b = new BarrierItemViewModel(this)
        {
            Label = SuggestLabel(),
            Name = "",
            Kind = BarrierKind.Secondary,
            Designation = BarrierDesignation.Wall,
            X1 = 500,
            Y1 = 100,
            X2 = 500,
            Y2 = 350,
            MaterialName = "Lead", //MaterialNames.FirstOrDefault() ?? "Lead",
            UseFactor = 1.0,
            Zone = "Uncontrolled",
            // PMGyWeek = 0.02,
            POverridden = false,
            N = 1,
            ApplyPreShield = false,
            ProtectedArea = "Office"
        };
        if (!IsPET && !IsCT)
            b.UseNcrpK = true;
        else
            b.UseNcrpK = false;
        b.InitDisplayName();
        b.RefreshLabelPosition();
        Barriers.Add(b);
        b.GetCanvasDistance_m = ComputeCanvasDistance_m;
        SelectedBarrier = b;
        AutoFillBarrierFromTables(b);
        UpdateCanvasExtent();
    }

    private bool _suppressLinkedBarrierOps;

    public void OnPrimaryLinkedSecondaryToggled(BarrierItemViewModel src, bool enabled)
    {
        if (_suppressLinkedBarrierOps) return;

        if (enabled)
        {
            // already exists?
            if (src.LinkedSecondaryId is Guid id && Barriers.Any(b => b.Id == id)) return;

            var clone = CloneBarrier(src, BarrierKind.Secondary, "_Sec");
            _suppressLinkedBarrierOps = true;
            try
            {
                src.LinkedSecondaryId = clone.Id;
            }
            finally { _suppressLinkedBarrierOps = false; }
        }
        else
        {
            if (src.LinkedSecondaryId is Guid id)
                RemoveBarrierById(id);

            _suppressLinkedBarrierOps = true;
            try { src.LinkedSecondaryId = null; }
            finally { _suppressLinkedBarrierOps = false; }
        }
    }

    public void OnSecondaryTwinToggled(BarrierItemViewModel src, bool enabled)
    {
        if (_suppressLinkedBarrierOps) return;

        if (enabled)
        {
            if (src.TwinSecondaryId is Guid id && Barriers.Any(b => b.Id == id)) return;

            var clone = CloneBarrier(src, BarrierKind.Secondary, "_T_W");
            _suppressLinkedBarrierOps = true;
            try
            {
                src.TwinSecondaryId = clone.Id;
            }
            finally { _suppressLinkedBarrierOps = false; }
        }
        else
        {
            if (src.TwinSecondaryId is Guid id)
                RemoveBarrierById(id);

            _suppressLinkedBarrierOps = true;
            try { src.TwinSecondaryId = null; }
            finally { _suppressLinkedBarrierOps = false; }
        }
    }

    private BarrierItemViewModel CloneBarrier(BarrierItemViewModel src, BarrierKind newKind, string suffix)
    {
        // label rule: if no label, just suffix (e.g. "_Sec")
        var baseLabel = (src.Label ?? "").Trim();
        var newLabel = string.IsNullOrWhiteSpace(baseLabel) ? suffix : baseLabel + suffix;

        var b = new BarrierItemViewModel(this)
        {
            Label = newLabel,
            Name = src.Name,

            Kind = newKind,
            Designation = src.Designation,

            X1 = src.X1,
            Y1 = src.Y1,
            X2 = src.X2,
            Y2 = src.Y2,

            MaterialName = src.MaterialName,
            UseFactor = src.UseFactor,
            Occupancy = src.Occupancy,
            Zone = src.Zone,

            PMGyWeek = src.PMGyWeek,
            POverridden = src.POverridden,
            N = src.N,

            // make sure the cloned "secondary" is truly secondary behavior-wise
            ApplyPreShield = false,
            PreShieldType = src.PreShieldType,
            ProtectedArea = src.ProtectedArea,

            UseNcrpK = src.UseNcrpK,
            K1 = src.K1,
            K1Text = src.K1Text,
            KSource = src.KSource,

            IsCtTertiary = src.IsCtTertiary,
            TertiaryHeight_m = src.TertiaryHeight_m,
            TertiaryHeightText = src.TertiaryHeightText,

            SnapToSource = src.SnapToSource,
            IsHiddenOnCanvas = src.IsHiddenOnCanvas,
            IsLabelHidden = src.IsLabelHidden,

            ScatterIsoToMeasurement_m = src.ScatterIsoToMeasurement_m,
            ScatterRate_mGyPerHourAt1m = src.ScatterRate_mGyPerHourAt1m,
            ScatterUGyPerMasAt1m = src.ScatterUGyPerMasAt1m,
            ScatterUGyPerGyCm2At1m = src.ScatterUGyPerGyCm2At1m,

            PetRegion = src.PetRegion,
        };

        // Copy saved distance behavior
        b.DistanceOverride_m = src.DistanceOverride_m;
        b.SavedRawDistance_m = src.SavedRawDistance_m;
        b.SaveDistance = src.SaveDistance;

        // Copy preexisting layers (deep copy)
        foreach (var layer in src.PreexistingLayers)
        {
            b.PreexistingLayers.Add(new PreexistingLayerViewModel
            {
                MaterialName = layer.MaterialName,
                ThicknessValue = layer.ThicknessValue,
                ThicknessUnit = layer.ThicknessUnit
            });
        }

        b.InitDisplayName();
        b.RefreshLabelPosition();
        b.GetCanvasDistance_m = ComputeCanvasDistance_m;

        // insert immediately BELOW the source barrier in the list
        var idx = Barriers.IndexOf(src);
        if (idx < 0) Barriers.Add(b);
        else Barriers.Insert(idx + 1, b);

        // optional: auto-fill from tables if you want it to refresh defaults for secondary too
        AutoFillBarrierFromTables(b);

        UpdateCanvasExtent();
        return b;
    }

    private void RemoveBarrierById(Guid id)
    {
        var b = Barriers.FirstOrDefault(x => x.Id == id);
        if (b == null) return;

        // if we're removing a linked child, clear any parent toggle pointing to it
        foreach (var p in Barriers)
        {
            if (p.LinkedSecondaryId == id)
            {
                _suppressLinkedBarrierOps = true;
                try { p.HasLinkedSecondary = false; p.LinkedSecondaryId = null; }
                finally { _suppressLinkedBarrierOps = false; }
            }

            if (p.TwinSecondaryId == id)
            {
                _suppressLinkedBarrierOps = true;
                try { p.HasTwinSecondary = false; p.TwinSecondaryId = null; }
                finally { _suppressLinkedBarrierOps = false; }
            }
        }

        b.Dispose();
        Barriers.Remove(b);
        if (ReferenceEquals(SelectedBarrier, b)) SelectedBarrier = null;
    }

    private static double DensityScale(string material, double rho)
    {
        // scale to Std Concrete as the reference when using Concrete coeffs
        if (material.Equals("Lite Concrete", StringComparison.OrdinalIgnoreCase))
        {
            const double rhoStd = 2.35; // g/cm3
            if (rho > 0) return rho / rhoStd; // e.g., 1.76 / 2.35 ≈ 0.75 → needs MORE thickness for same effect
        }

        return 1.0;
    }
    private static string CoeffMaterialName(string material)
    {
        if (string.IsNullOrWhiteSpace(material)) return "Lead";
        var m = material.Trim();

        if (m.Equals("Lead-Eq", StringComparison.OrdinalIgnoreCase)) return "Lead";
        if (m.Equals("Std Concrete", StringComparison.OrdinalIgnoreCase)) return "Concrete";
        if (m.Equals("Standard Concrete", StringComparison.OrdinalIgnoreCase)) return "Concrete";
        if (m.Equals("Lite Concrete", StringComparison.OrdinalIgnoreCase)) return "Concrete"; // density-scaled
        if (m.Equals("Gypsum", StringComparison.OrdinalIgnoreCase)) return "Gypsum Wallboard";
        if (m.Equals("Gypsum Wallboard", StringComparison.OrdinalIgnoreCase)) return "Gypsum Wallboard";

        return m;
    }
    private MaterialSpec ResolveMaterialSpecFor(string materialName, bool primaryLike, BarrierItemViewModel b)
    {
        var matNameRaw = string.IsNullOrWhiteSpace(materialName) ? "Lead" : materialName.Trim();
        var matNameForCoeffs = CoeffMaterialName(matNameRaw);

        // carry density from user-visible material
        var baseMat = _data.Materials.TryGetValue(matNameRaw, out var m) ? m : null;
        var density = baseMat?.Density_g_cm3 ?? 0;

        var spec = new MaterialSpec
        {
            Name = matNameRaw,
            Density_g_cm3 = density,
            Alpha = double.NaN,
            Beta = double.NaN,
            Gamma = double.NaN
        };

        var scenario = ScenarioFor(b, primaryLike);

        bool TryFillScenario(string key)
        {
            (double a, double bcoef, double g) c;
            bool ok = primaryLike
                ? _data.TryGetPrimaryCoeff(scenario, key, out c)
                : _data.TryGetSecondaryCoeff(scenario, key, out c);

            if (ok && c.a > 0 && c.g > 0)
            {
                spec = new MaterialSpec
                {
                    Name = $"{matNameRaw} [{scenario}]",
                    Density_g_cm3 = density,
                    Alpha = c.a,
                    Beta = c.bcoef,
                    Gamma = c.g
                };
                TraceItems.Add(new("Coeff hit", $"{key} ({(primaryLike ? "primary" : "secondary")}, {scenario})"));
                return true;
            }
            return false;
        }

        // try mapped then raw
        if (!TryFillScenario(matNameForCoeffs))
            TryFillScenario(matNameRaw);

        // CT override: try mapped then raw
        if (IsCT)
        {
            bool TryFillCt(string key)
            {
                if (_data.TryGetCtCoeff(CtKvp, key, out var cCT) && cCT.a > 0 && cCT.g > 0)
                {
                    spec = new MaterialSpec
                    {
                        Name = spec.Name,
                        Density_g_cm3 = density,
                        Alpha = cCT.a,
                        Beta = cCT.b,
                        Gamma = cCT.g
                    };
                    TraceItems.Add(new("CT coeff hit", $"{key} (kVp={CtKvp})"));
                    return true;
                }
                return false;
            }
            if (!TryFillCt(matNameForCoeffs))
                TryFillCt(matNameRaw);
        }

        return spec;
    }

    // Archer transmission for a single layer (thickness in mm)
    // Consistent with inversion in the solver; t in mm
    private static double ArcherTransmission(double alpha, double beta, double gamma, double t_mm)
    {
        if (!(alpha > 0) || !(gamma > 0) || !(t_mm >= 0) || double.IsNaN(t_mm)) return 1.0;

        double ratio = beta / alpha;  // Can be <0 (OK)
        double outer = 1 + ratio;
        double midder = Math.Exp(alpha * gamma * t_mm);
        double inner = ratio;  // ← **CHANGE 1: Subtract r, NOT pow(r, -1/γ)**
        double inside = outer * midder - inner;  // ← **CHANGE 2: Rename for clarity**

        // Validate intermediate (physical: inside >=1)
        if (!(inside > 0) || double.IsNaN(inside) || double.IsInfinity(inside)) return 1.0;

        double T = Math.Pow(inside, -1.0 / gamma);  // ← **CHANGE 3: Final T**

        // Clamp T (transmission)
        if (double.IsNaN(T) || double.IsInfinity(T)) return 1.0;
        if (T < 1e-12) T = 1e-12;
        if (T > 1.0) T = 1.0;  // ← **CHANGE 4: Clamp T <=1**

        return T;
    }


    // Multiply transmissions for all pre-existing layers using each layer’s own α/β/γ
    private double PreStackTransmission(BarrierItemViewModel b, bool primaryLike)
    {
        if (b.PreexistingLayers.Count == 0) return 1.0;

        double B = 1.0;
        foreach (var layer in b.PreexistingLayers)
        {
            if (layer == null) continue;
            var name = layer.MaterialName ?? "";
            if (string.IsNullOrWhiteSpace(name) ||
                string.Equals(name, "None", StringComparison.OrdinalIgnoreCase)) continue;

            var t_mm = layer.Thickness_mm;
            if (!(t_mm > 0))
            {
                TraceItems.Add(new("Pre layer skipped", $"{name}: t≤0"));
                continue;
            }

            // Use the correct coeffs for THIS material & scenario
            var spec = ResolveMaterialSpecFor(name, primaryLike, b);
            if (!(spec.Alpha > 0) || !(spec.Gamma > 0))
            {
                TraceItems.Add(new("Pre layer skipped (no coeffs)", name));
                continue;
            }

            // Keep your existing density-based thickness scaling convention
            var rho = _data.Materials.TryGetValue(name, out var m) ? m.Density_g_cm3 : 0;
            var scale = DensityScale(name, rho);
            var t_eff_mm = t_mm * scale;

            TraceItems.Add(new("Specs:", $"a={spec.Alpha}: b={spec.Beta:F2}, g={spec.Gamma:G6}"));
            var Bi = ArcherTransmission(spec.Alpha, spec.Beta, spec.Gamma, t_eff_mm);
            B *= Bi;

            TraceItems.Add(new("Pre layer B",
                $"{name}: t={t_mm:F2} mm, B={Bi:G6}"));
        }

        return B;
    }

    // -----------------------  Compute thickness  -----------------------

    private static double SolveThicknessFromArcher(double a, double b, double g, double B)
    {
        if (!(a > 0) || !(g > 0)) return double.NaN;
        B = Math.Clamp(B, 1e-12, 1.0);              // avoid 0 or >1
        double r = b / a;
        double den = 1.0 + r;
        double num = Math.Pow(B, -g) + r;
        if (den <= 0 || num <= 0) return double.NaN;
        double t = Math.Log(num / den) / (a * g);
#if NET5_0_OR_GREATER
        return (double.IsFinite(t) && t >= 0) ? t : double.NaN;
#else
    return (!double.IsNaN(t) && !double.IsInfinity(t) && t >= 0) ? t : double.NaN;
#endif
    }

    private void AddPetTraceHeader(BarrierItemViewModel b, double kU, double kP, double kUsed, double N_eff, double r_m, double Kweek)
    {
        //TraceItems.Add(new("Modality", "PET / NM (TG-108)"));
        //TraceItems.Add(new("Suite mode", string.Equals(SelectedRoomCategory, "PET/CT Suite (Uptake + PET)", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No"));
        // TraceItems.Add(new("K source", b.KSource ?? "—"));
        TraceItems.Add(new("Isotope", SelectedIsotope.ToString()));
        TraceItems.Add(new("Half-life (h)", (PetTg108.HalfLife_min(SelectedIsotope) / 60.0).ToString("G6")));
        //   TraceItems.Add(new("Admin activity (MBq)", PetAdmin_MBq.ToString("G6")));
        //    TraceItems.Add(new("Uptake time (min)", PetUptake_min.ToString("G6")));
        //    TraceItems.Add(new("Imaging time (min)", PetImaging_min.ToString("G6")));
        //   TraceItems.Add(new("Voiding factor", PetVoidingFactor.ToString("G6")));

        TraceItems.Add(new("Barrier PET region", b.PetRegion.ToString()));


        TraceItems.Add(new("K1 Uptake @1m (mGy/pt)", kU.ToString("G6")));
        TraceItems.Add(new("K1 Image @1m (mGy/pt)", kP.ToString("G6")));
        TraceItems.Add(new("K1 used @1m (mGy/pt)", kUsed.ToString("G6")));

        //  TraceItems.Add(new("Patients/week", N_eff.ToString("G6")));
        TraceItems.Add(new("Distance used r (m)", r_m.ToString("G6")));
        // TraceItems.Add(new("Use factor U", b.UseFactor.ToString("G6")));
        //  TraceItems.Add(new("Occupancy T", b.Occupancy.ToString("G6")));
        //  TraceItems.Add(new("P (mGy/wk)", b.PMGyWeek.ToString("G6")));

        TraceItems.Add(new("K_week @ barrier (mGy/wk)", Kweek.ToString("G6")));
    }

    [ObservableProperty] private string petPatientDoseConst_Text = "0.092";
    [ObservableProperty] private double petPatientDoseConst_uSv_m2_per_MBq_h = 0.092;
    [ObservableProperty] private bool petPatientDoseConstManual;

    partial void OnPetPatientDoseConst_TextChanged(string value)
    {
        var d = ParseOrZero(value);
        if (PetPatientDoseConst_uSv_m2_per_MBq_h != d)
            PetPatientDoseConst_uSv_m2_per_MBq_h = d;

    }

    partial void OnPetPatientDoseConst_uSv_m2_per_MBq_hChanged(double value)
    {
        var s = value.ToString("0.####", CultureInfo.CurrentCulture);
        if (PetPatientDoseConst_Text != s)
            PetPatientDoseConst_Text = s;

        RecomputeIfPET();
    }

    partial void OnPetPatientDoseConstManualChanged(bool value)
    {
        RecomputeIfPET();
    }

    private const double TG108_F18_PatientConst = 0.092;

    private double DefaultPetPatientConst_uSv_m2_per_MBq_h =>
    PetTg108.GammaPatient_uSv_m2_per_MBq_h(SelectedIsotope, TG108_F18_PatientConst);

    public double PetPatientDoseConstUsed_uSv_m2_per_MBq_h =>
        (PetPatientDoseConstManual && PetPatientDoseConst_uSv_m2_per_MBq_h > 0)
            ? PetPatientDoseConst_uSv_m2_per_MBq_h
            : DefaultPetPatientConst_uSv_m2_per_MBq_h;


    public ObservableCollection<string> PreexistingMaterialNamesPet { get; } =
 new() { "None", "Lead", "Concrete", "Iron" };

    public IEnumerable<string> PreexistingMaterialNamesForUi
    => IsPET ? PreexistingMaterialNamesPet : PreexistingMaterialNames;

    private void EnforcePetPreLayersIfNeeded()
    {
        if (!IsPET) return;

        foreach (var b in Barriers)
            foreach (var pl in b.PreexistingLayers)
            {
                var n = pl.MaterialName ?? "None";
                if (!PreexistingMaterialNamesPet.Contains(n))
                    pl.MaterialName = "Lead"; // or "None"
            }
    }
    private static bool TryMapPetMat(string? name, out PetShieldMaterial mat)
    {
        mat = PetShieldMaterial.Lead;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        var s = name.Trim();

        if (s.Equals("None", StringComparison.OrdinalIgnoreCase))
            return false;

        if (s.Equals("Lead", StringComparison.OrdinalIgnoreCase))
        {
            mat = PetShieldMaterial.Lead;
            return true;
        }

        if (s.Equals("Concrete", StringComparison.OrdinalIgnoreCase))
        {
            mat = PetShieldMaterial.Concrete;
            return true;
        }

        if (s.Equals("Iron", StringComparison.OrdinalIgnoreCase))
        {
            mat = PetShieldMaterial.Iron;
            return true;
        }

        return false;
    }


    private double PetPreStackTransmission(BarrierItemViewModel b)
    {
        if (b.PreexistingLayers.Count == 0) return 1.0;

        double B = 1.0;
        foreach (var layer in b.PreexistingLayers)
        {
            if (layer == null) continue;
            if (!TryMapPetMat(layer.MaterialName, out var m)) continue;

            var t_mm = layer.Thickness_mm;
            if (!(t_mm > 0)) continue;

            var Bi = PetAttenuation.TransmissionFromThickness(t_mm, m);
            B *= Bi;
        }
        return Math.Clamp(B, 1e-12, 1.0);
    }

    private void RefreshBarrierDistance(BarrierItemViewModel b)
    {
        if (b == null) return;

        double base_m;

        // 1) Explicit override always wins
        if (b.DistanceOverride_m > 0)
        {
            base_m = b.DistanceOverride_m;
        }
        else
        {
            // 2) Rule-based distance (if applicable)
            var ruleDist = GetRuleDistance_m(b);

            if (ruleDist.HasValue && ruleDist.Value > 0)
            {
                base_m = ruleDist.Value;
            }
            else
            {
                // 3) Saved distance (if locked)
                if (b.SaveDistance && b.SavedRawDistance_m > 0)
                    base_m = b.SavedRawDistance_m;
                else
                    base_m = ComputeCanvasDistance_m(b);
            }
        }

        b.AutoDistance_m = base_m;
        b.DisplayDistance_m = base_m;

        // ensures EffectiveDistance_m updates + downstream bindings fire
        b.RaiseEffectiveDistanceChanged();
    }

    private void ComputeBarrier(BarrierItemViewModel b)
    {
        // distance from drawing (fresh each time)
        //RecomputeDistancesForAll();
        if (b == null)
        {
          //  CacheTraceToBarrier(b);
            return;
        }

        // NEW: refresh distance only for this barrier
        RefreshBarrierDistance(b);

        TraceItems.Clear();
        var kResolved = ResolveK1For(b, true);
        if (!(b.K1ManualOverride))
        {
            b.SetK1FromEngine(kResolved);
        }
        else
        {
            b.KSource = "Manual";
        }

        double N_eff = PatientsPerWeek;

        if (IsPET)
        {
            // Recompute K1 pieces for trace clarity
            double kPatient = PetPatientDoseConstUsed_uSv_m2_per_MBq_h;
            //double hl = PetTg108.HalfLife_min(SelectedIsotope);
            var kU = PetTg108.K1_Uptake_mGy_perPt_at1m(kPatient, SelectedIsotope, PetAdmin_MBq, PetUptake_min);
            var kP = PetTg108.K1_PetRoom_mGy_perPt_at1m(kPatient, SelectedIsotope, PetAdmin_MBq, PetUptake_min, PetImaging_min, PetVoidingFactor);
            var mat = (b.MaterialName ?? "").Trim();
            PetShieldMaterial petMat =
    mat.StartsWith("Lead", StringComparison.OrdinalIgnoreCase) ? PetShieldMaterial.Lead :
    mat.StartsWith("Concrete", StringComparison.OrdinalIgnoreCase) ? PetShieldMaterial.Concrete :
    mat.StartsWith("Iron", StringComparison.OrdinalIgnoreCase) ? PetShieldMaterial.Iron :
    // mat.StartsWith("Lead Glass", StringComparison.OrdinalIgnoreCase) ? PetShieldMaterial.LeadGlass :
    PetShieldMaterial.Lead; // safe default
            // b.K1 should already be the chosen “used” K1 @1m (per your ResolveK1For logic)
            var kUsed = b.K1;

            double r = Math.Max(1e-6, b.EffectiveDistance_m);
            // TG-108: PET patient source is isotropic — use factor is always 1.0
            double Kweek = (kUsed * N_eff) / (r * r) * b.Occupancy;
            double B_total_needed = (Kweek > 0) ? Math.Clamp(b.PMGyWeek / Kweek, 1e-12, 1.0) : 1.0;

            double B_pre = PetPreStackTransmission(b);

            // What your NEW shielding must provide (after the pre-existing already attenuated)
            double B_design_needed = Math.Clamp(B_total_needed / B_pre, 1e-12, 1.0);
            AddPetTraceHeader(b, kU, kP, kUsed, N_eff, r, Kweek);

            TraceItems.Add(new("B needed", B_design_needed.ToString("G6")));


            TraceItems.Add(new(
   "Patient dose const",
   $"{PetPatientDoseConstUsed_uSv_m2_per_MBq_h:G4} µSv·m²/(MBq·h)" +
   (PetPatientDoseConstManual ? " (manual)" : " (TG-108)")
));
            var p = PetAttenuation.GetArcherParams(petMat);
            TraceItems.Add(new("Archer αβγ", $"α={p.Alpha:G6}, β={p.Beta:G6}, γ={p.Gamma:G6}"));


            bool hasArcher = p.Alpha > 0 && p.Gamma > 0 && (1.0 + p.Beta / p.Alpha) > 0;
            TraceItems.Add(new("Attenuation model", hasArcher ? "Archer" : "TVL"));

            b.RequiredThickness_mm = PetAttenuation.ThicknessFromB_mm(B_design_needed, petMat);
            b.ApplyThicknessRounding();

            // ---- PET summary + cache (must run before return) ----
            var reqDispPet = !string.IsNullOrWhiteSpace(b.ThicknessShortDisplay)
                ? b.ThicknessShortDisplay
                : $"{b.RequiredThickness_mm:F2} mm";

            var finDispPet = !string.IsNullOrWhiteSpace(b.FinalThicknessShortDisplay)
                ? b.FinalThicknessShortDisplay
                : reqDispPet;

            ResultSummary = b.HasThicknessRounding
                ? $"{BareBarrierTitle(b)}: {b.MaterialName} → Req: {reqDispPet} -> Final: {finDispPet}"
                : $"{BareBarrierTitle(b)}: {b.MaterialName} → {reqDispPet}";

            CacheTraceToBarrier(b);
            return;
        }

        var matName = string.IsNullOrWhiteSpace(b.MaterialName) ? "Lead" : b.MaterialName;

        var matForBarrier = _data.Materials.TryGetValue(matName, out var baseMat)
            ? baseMat
            : new MaterialSpec { Name = matName, Density_g_cm3 = 0, Alpha = 1, Beta = 0, Gamma = 1 };

        // --- coefficient lookup (with scenario normalization) ---
        var scenario = ScenarioFor(b, b.Kind == BarrierKind.Primary);

        if (b.Kind == BarrierKind.Primary)
        {
            if (_data.TryGetPrimaryCoeff(scenario, matName, out var c))
                matForBarrier = new MaterialSpec
                {
                    Name = $"{matName} [{scenario}]",
                    Density_g_cm3 = baseMat?.Density_g_cm3 ?? 0,
                    Alpha = c.a,
                    Beta = c.b,
                    Gamma = c.g
                };
            else
                TraceItems.Add(new KeyValuePair<string, string>("Coeff lookup",
                    $"Missing for '{scenario}', using fallback."));
        }
        else
        {
            if (_data.TryGetSecondaryCoeff(scenario, matName, out var c))
                matForBarrier = new MaterialSpec
                {
                    Name = $"{matName} [{scenario}]",
                    Density_g_cm3 = baseMat?.Density_g_cm3 ?? 0,
                    Alpha = c.a,
                    Beta = c.b,
                    Gamma = c.g
                };
        }

        // ---- CT-specific α/β/γ override (from NCRP 147 Fig A.2 / A.3) ----
        if (IsCT)
        {
            if (_data.TryGetCtCoeff(CtKvp, matName, out var cCT))
            {
                matForBarrier = new MaterialSpec
                {
                    Name = matForBarrier.Name,
                    Density_g_cm3 = matForBarrier.Density_g_cm3,
                    Alpha = cCT.a,
                    Beta = cCT.b,
                    Gamma = cCT.g
                };
                // TraceItems.Add(new KeyValuePair<string, string>("CT αβγ", $"α={cCT.a:F4}, β={cCT.b:F4}, γ={cCT.g:F4}"));
            }
            else
            {
                TraceItems.Add(new KeyValuePair<string, string>("CT αβγ", "Missing; using secondary material coeffs."));
            }
        }

        // ---------- Pre-existing: sequential B-chaining (correct method) ----------
        var primaryOrLeakageLike = b.Kind == BarrierKind.Primary;
        // multiply transmissions of ALL pre-existing layers (each with its own coeffs)
        double B_preStack = PreStackTransmission(b, primaryOrLeakageLike);
        double CTk1sec = 0.0;
        double K_tertiary_wk = 0;
     //   bool useMulti = b.UseMultipleSources;// && b.SourceContributions.Any(sc => sc.IsEnabled);

        // ======================================================
        // CT: tertiary only
        // ======================================================
        if (IsCT)
        {
            bool includeSutton = (CtMode != CtCalcMode.ScatterMapOnly);
            bool baseUsesDlp = (CtMode == CtCalcMode.DlpPlusSutton);

            // Base K1 is ALREADY b.K1 (mGy/pt @ 1 m) from ResolveK1For
            TraceItems.Add(new("CT mode", CtMode.ToString()));
            TraceItems.Add(new("CT base (mGy/pt @1m)", b.K1.ToString("G6")));

            // Only flag CTk1sec when DLP is the base
            CTk1sec = baseUsesDlp ? b.K1 : 0.0;
            TraceItems.Add(new("DLP (k1sec mGy/pt)", baseUsesDlp ? CTk1sec.ToString("G6") : "—"));

            // --- tertiary stays independent (weekly, at barrier) ---
       //     double K_tertiary_yr = 0.0;

            if (includeSutton && b.IsCtTertiary && _data?.CTSutton is { } sutton)
            {
                var kvpKey = CtKvp == "140" ? "140" : "120";
                if (sutton.TryGetValue(kvpKey, out var s))
                {
                    var b_h = b.TertiaryHeight_m > 0 ? b.TertiaryHeight_m : 1.0;
                    var b_c = FtToM(RoomHeight_ft);

                    double totalExams = CtDlpExamCountForWeighting;
                    double contrastFactor = CtContrastFactor;

                    // Weekly body/head DLP totals (mGy·cm/wk)
                    double dlpBodyWeek_mGycm = CtDlpBodyPerPatient_mGycm * CtBodyScansPerWeek;
                    double dlpHeadWeek_mGycm = CtDlpHeadPerPatient_mGycm * CtHeadScansPerWeek;

                    // Martin 2012 tertiary workload term:
                    // DLP_body + 0.5 * DLP_head
                    double dlpTertiaryWeek_mGycm_base = dlpBodyWeek_mGycm + 0.5 * dlpHeadWeek_mGycm;
                    double dlpTertiaryWeek_mGycm = dlpTertiaryWeek_mGycm_base * contrastFactor;

                    // Convert weekly DLP to Gy·cm/wk for the Sutton/Martin tertiary fit
                    double dlpTertiaryWeek_Gycm = dlpTertiaryWeek_mGycm / 1000.0;

                    // Raw tertiary from fit is µGy/wk
                    double K_tertiary_uGy_wk = CtTertiary.ComputeK_Tertiary_uGy_wk(
                        b_h, b_c, dlpTertiaryWeek_Gycm, s.m_a, s.m_b, s.c_a, s.c_b);

                    // Convert to mGy/wk for shielding solver
                    K_tertiary_wk = K_tertiary_uGy_wk / 1000.0;

                    TraceItems.Add(new("CT DLP body scans/wk", CtBodyScansPerWeek.ToString("G6")));
                    TraceItems.Add(new("CT DLP head scans/wk", CtHeadScansPerWeek.ToString("G6")));
                    TraceItems.Add(new("CT DLP total exams/wk", totalExams.ToString("G6")));

                    TraceItems.Add(new("CT DLP body week (mGy·cm/wk)", dlpBodyWeek_mGycm.ToString("G6")));
                    TraceItems.Add(new("CT DLP head week (mGy·cm/wk)", dlpHeadWeek_mGycm.ToString("G6")));
                    TraceItems.Add(new("CT DLP tertiary base (mGy·cm/wk)",
                        $"body + 0.5×head = {dlpTertiaryWeek_mGycm_base:G6}"));

                    if (CtApplyContrastWeight)
                    {
                       // TraceItems.Add(new("CT contrast (%)", CtContrastPercent.ToString("G6")));
                        TraceItems.Add(new("CT contrast factor", contrastFactor.ToString("G6")));
                        TraceItems.Add(new("CT DLP tertiary adj. (mGy·cm/wk)",
                            $"{dlpTertiaryWeek_mGycm_base:G6} × {contrastFactor:G6} = {dlpTertiaryWeek_mGycm:G6}"));
                    }
                    else
                    {
                        TraceItems.Add(new("CT contrast factor", "1.0"));
                        TraceItems.Add(new("CT DLP tertiary adj. (mGy·cm/wk)", dlpTertiaryWeek_mGycm.ToString("G6")));
                    }

                  //  TraceItems.Add(new("CT DLP tertiary (Gy·cm/wk)", dlpTertiaryWeek_Gycm.ToString("G6")));
                  //  TraceItems.Add(new("CT tertiary raw (µGy/wk)", K_tertiary_uGy_wk.ToString("G6")));
                    TraceItems.Add(new("CT tertiary final (mGy/wk)", K_tertiary_wk.ToString("G6")));
                    TraceItems.Add(new("Sutton coeffs", $"ma={s.m_a}, mb={s.m_b}, ca={s.c_a}, cb={s.c_b}"));
                }
                else
                {
                    TraceItems.Add(new("CT tertiary", $"Missing Sutton params for {kvpKey} kV"));
                }
            }
            else
            {
                if (b.IsCtTertiary && CtMode == CtCalcMode.ScatterMapOnly)
                    TraceItems.Add(new("CT tertiary", "OFF (Scatter map only)"));
            }

            if (CtMode == CtCalcMode.DlpPlusSutton)
                N_eff = CtScansPerWeekTotal;   // match CT weekly workload used for DLP path
            else
                N_eff = PatientsPerWeek;       // keep old behavior for scatter-map-only mode
        }



        // Catalog pre-shield (same-material) stays as-is
        var catalogPre_mm = b.Kind == BarrierKind.Primary && b.ApplyPreShield ? b.PreShield_mm : 0.0;

        // Use per-patient K1 through the pre-stack, N is still patients/week
        var reqWithPre = new BarrierRequest
        {
            BarrierId = b.Name,
            MaterialName = matName,
            Distance_m = b.EffectiveDistance_m,
            UseFactor = b.UseFactor,
            Occupancy = b.Occupancy,
            PermissibleP_mGy_week = b.PMGyWeek,
            K1 = Math.Max(1e-12, b.K1 * B_preStack),  // <-- per-patient AFTER pre-layers
            N = N_eff, //CtHasDlpWeek ? CtScansPerWeekTotal : PatientsPerWeek,
            PreShield_mm = catalogPre_mm,             // same-material only
            IsPrimary = b.Kind == BarrierKind.Primary,
        };


        var baseResult = ShieldingCalculator.Compute(reqWithPre, matForBarrier, K_tertiary_wk);
        // 1) Keep the solver’s answer if sane
        double t_req = baseResult.RequiredThickness_mm;
        bool tertiaryBlocksDesign = false;

        if (double.IsPositiveInfinity(t_req))
        {
            tertiaryBlocksDesign = true;
            TraceItems.Add(new("CT tertiary check",
                "Tertiary alone exceeds the permissible goal"));

            // keep going so full trace still gets appended
        }
        // read Keff (weekly at barrier with U·T)

        baseResult.Trace.TryGetValue("K_base_week @barrier (mGy/wk)", out var K_base_week_eff);
        baseResult.Trace.TryGetValue("K_ter_week @barrier (mGy/wk)", out var K_ter_week_eff);
        baseResult.Trace.TryGetValue("P_eff (mGy/wk)", out var P_eff_trace);
        // trigger fallback if solver failed OR gave 0 while base still exceeds effective limit
        bool needFallback =
            (!double.IsFinite(t_req) || t_req < 0) ||
            (t_req <= 0 && P_eff_trace > 0 && K_base_week_eff > P_eff_trace + 1e-12);

        if (needFallback)
        {
            if (P_eff_trace <= 0)
            {
                TraceItems.Add(new("Fallback", "Tertiary alone exceeds permissible weekly design goal."));
                t_req = double.PositiveInfinity;
            }
            else if (K_base_week_eff > 0)
            {
                // reqWithPre.K1 already includes pre-existing layers,
                // so K_base_week_eff is already AFTER pre-stack.
                double B_needed_design = Math.Min(1.0, Math.Max(1e-12, P_eff_trace / K_base_week_eff));

                double t_inv = SolveThicknessFromArcher(
                    matForBarrier.Alpha, matForBarrier.Beta, matForBarrier.Gamma, B_needed_design);

                if (
#if NET5_0_OR_GREATER
                    double.IsFinite(t_inv)
#else
            !double.IsNaN(t_inv) && !double.IsInfinity(t_inv)
#endif
                    && t_inv >= 0)
                {
                    t_req = t_inv;
                    TraceItems.Add(new("Fallback", "Archer inverse used with P_eff (solver out-of-range)."));
                }
            }
        }
        b.RequiredThickness_mm = double.IsNaN(t_req) ? double.NaN : t_req;
        b.ApplyThicknessRounding();

        // Trace (kept consistent with your style)
        TraceItems.Add(new("K_perPt @1m (mGy/pt)", $"{b.K1:F3}"));
        TraceItems.Add(new("K_week @1m (mGy/wk)", $"{b.K1 * N_eff:F3}"));

        TraceItems.Add(new("Pre-stack transmission B", B_preStack.ToString("G6")));
        // TraceItems.Add(new("K1 used after pre-stack (mGy/pt @1m)", (b.K1 * B_preStack).ToString("G6")));

        TraceItems.Add(new("K source", b.KSource ?? "—"));
        foreach (var kv in baseResult.Trace)
        {
            var key = kv.Key?.Trim();
            if (string.Equals(key, "Protected area", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "Barrier id", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(key, "P_eff (mGy/wk) [P - k_ter]", StringComparison.OrdinalIgnoreCase))
            {
                var pEff = kv.Value;

                TraceItems.Add(new(
                    key ?? "?",
                    pEff < 0
                        ? $"Exceeded by tertiary ({pEff:G6})"
                        : $"{pEff:G6}"));

                continue;
            }

            TraceItems.Add(new(key ?? "?", kv.Value.ToString("G6")));
        }

        // ---- Unified summary ----
        var reqDisp = !string.IsNullOrWhiteSpace(b.ThicknessShortDisplay)
            ? b.ThicknessShortDisplay
            : $"{b.RequiredThickness_mm:F2} mm";

        var finDisp = !string.IsNullOrWhiteSpace(b.FinalThicknessShortDisplay)
            ? b.FinalThicknessShortDisplay
            : reqDisp;

        if (tertiaryBlocksDesign)
        {
            ResultSummary = $"{BareBarrierTitle(b)}: {b.MaterialName} → Not solvable (tertiary exceeds limit)";
        }
        else
        {
            ResultSummary = b.HasThicknessRounding
                ? $"{BareBarrierTitle(b)}: {b.MaterialName} → Req: {reqDisp} -> Final: {finDisp}"
                : $"{BareBarrierTitle(b)}: {b.MaterialName} → {reqDisp}";
        }

        CacheTraceToBarrier(b);
    }


    partial void OnSelectedRoomCategoryChanged(string? value)
    {
        if (IsLoadingProject) return;
        var suppress = !HasActiveDesign;
        using (suppress ? SuspendDirty() : null)
        {
        OnPropertyChanged(nameof(IsXrayRoom));
        OnPropertyChanged(nameof(IsCT));
        OnPropertyChanged(nameof(IsPET));
        OnPropertyChanged(nameof(PreexistingMaterialNamesForUi));
        EnforcePetPreLayersIfNeeded();
        OnPropertyChanged(nameof(MaterialNamesForUi));
        EnforcePetMaterialOnAllBarriersIfNeeded();

            BackgroundImage = null;
            BackgroundOpacity = 0.0;
            BackgroundScale = 1.0;
            BackgroundOffsetX = 0.0;
            BackgroundOffsetY = 0.0;
            IsEditingBackground = false;

            LoadDefaultLayoutForRoom();
        ApplyCategoryExamPresets();
        ApplyNcrpPatientsIfLocked();

        // If no category yet, just exit gracefully
        if (string.IsNullOrWhiteSpace(SelectedRoomCategory))
            return;
        var secRoom = IsTypicallySecondaryRoom(SelectedRoomCategory); // now nullable-safe
        foreach (var pb in Barriers.Where(x => x.Kind == BarrierKind.Primary))
            if (secRoom && pb.UseNcrpK)
                pb.UseNcrpK = false;


        if (IsCT)
        {
            ApplyCtDefaults();
            ApplyCtAutoMap();
            if (string.IsNullOrEmpty(CtKvp))
                CtKvp = "120";
        }
        else
        {
            foreach (var b in Barriers) b.IsCtTertiary = false;
        }

        if (IsPET)
        {
            foreach (var b in Barriers)
            {
                b.UseNcrpK = false;
                b.SetK1FromEngine(0.0);
                b.KSource = "PET";
                b.NotifyPetRoomStateChanged();
            }
        }
        else
        {
            foreach (var b in Barriers)
                b.NotifyPetRoomStateChanged();
        }

        if (IsPET)
        {
            foreach (var b in Barriers)
                foreach (var pl in b.PreexistingLayers)
                    if (!PreexistingMaterialNamesPet.Contains(pl.MaterialName))
                        pl.MaterialName = "Concrete"; // or "Lead"
        }
    }
        if (!HasActiveDesign)
            ClearDirty();
    }

   
    // === Survey inputs ===
    // Mode A: rate-based (e.g., mR/h behind barrier)
    [ObservableProperty] private double surveyRate_mR_per_h;
    [ObservableProperty] private double surveyOnTime_h_per_week = 40;
    [ObservableProperty] private string surveyRate_mR_per_h_Text = "";
    [ObservableProperty] private string surveyOnTime_h_per_week_Text = "40";

    // Mode B: per-exam cumulative (e.g., mR per exam, dental/mammo)
    [ObservableProperty] private double surveyPerExam_mR;
    [ObservableProperty] private double surveyExams_per_week = 100;

    // NEW: string proxies that the TextBoxes bind to
    [ObservableProperty] private string surveyPerExam_mR_Text = "";
    [ObservableProperty] private string surveyExams_per_week_Text = "100";

    // Common modifiers
    [ObservableProperty] private double surveyUseFactor_U = 1.0;
    [ObservableProperty] private double surveyOccupancy_T = 1.0;
    [ObservableProperty] private string surveyUseFactor_U_Text = "1.0";
    [ObservableProperty] private string surveyOccupancy_T_Text = "1.0";

    [ObservableProperty] private bool surveyControlledArea;

    public int SurveyControlledAreaIndex
    {
        get => SurveyControlledArea ? 1 : 0;
        set
        {
            SurveyControlledArea = value == 1;
            OnPropertyChanged();
            NotifySurveyChanged();
        }
    }

    // Derived
    public double SurveyWeekly_mGy_fromRate
        => SurveyRate_mR_per_h * SurveyOnTime_h_per_week * 0.00876;

    public double SurveyWeekly_mGy_fromPerExam
        => SurveyPerExam_mR * SurveyExams_per_week * 0.00876;

    // Apply U and T (conservative: multiply the weekly air kerma by U*T)
    public double SurveyWeeklyP_mGy
    {
        get
        {
            // choose the larger of the two modes if both provided; or you can pick one via a checkbox
            var wk = Math.Max(SurveyWeekly_mGy_fromRate, SurveyWeekly_mGy_fromPerExam);
            return wk * SurveyUseFactor_U * SurveyOccupancy_T;
        }
    }

    public double SurveyLimit_mGy_per_week => SurveyControlledArea ? 0.1 : 0.02;
    public bool SurveyPass => SurveyWeeklyP_mGy <= SurveyLimit_mGy_per_week;

    // A tiny status string you can bind
    public string SurveyStatus
        => $"{SurveyWeeklyP_mGy:F3} mGy/wk vs limit {SurveyLimit_mGy_per_week:F3} → " +
           (SurveyPass ? "PASS" : "FAIL");

    // Bump all computed survey fields at once
    private void NotifySurveyChanged()
    {
        OnPropertyChanged(nameof(SurveyWeekly_mGy_fromRate));
        OnPropertyChanged(nameof(SurveyWeekly_mGy_fromPerExam));
        OnPropertyChanged(nameof(SurveyWeeklyP_mGy));
        OnPropertyChanged(nameof(SurveyLimit_mGy_per_week));
        OnPropertyChanged(nameof(SurveyPass));
        OnPropertyChanged(nameof(SurveyStatus));
    }

    partial void OnSurveyRate_mR_per_h_TextChanged(string value)
    {
        SurveyRate_mR_per_h = ParseOrZero(value);
    }

    partial void OnSurveyOnTime_h_per_week_TextChanged(string value)
    {
        SurveyOnTime_h_per_week = ParseOrZero(value);
    }

    partial void OnSurveyPerExam_mR_TextChanged(string value)
    {
        SurveyPerExam_mR = ParseOrZero(value);
    }

    partial void OnSurveyExams_per_week_TextChanged(string value)
    {
        SurveyExams_per_week = ParseOrZero(value);
    }

    partial void OnSurveyRate_mR_per_hChanged(double value)
    {
        SurveyRate_mR_per_h_Text = value.ToString("0.####", CultureInfo.CurrentCulture);
        NotifySurveyChanged();
    }

    partial void OnSurveyOnTime_h_per_weekChanged(double value)
    {
        SurveyOnTime_h_per_week_Text = value.ToString("0.####", CultureInfo.CurrentCulture);
        NotifySurveyChanged();
    }

    partial void OnSurveyPerExam_mRChanged(double value)
    {
        SurveyPerExam_mR_Text = value.ToString("0.####", CultureInfo.CurrentCulture);
        NotifySurveyChanged();
    }

    partial void OnSurveyExams_per_weekChanged(double value)
    {
        SurveyExams_per_week_Text = value.ToString("0.####", CultureInfo.CurrentCulture);
        NotifySurveyChanged();
    }

    partial void OnSurveyUseFactor_U_TextChanged(string value)
    {
        SurveyUseFactor_U = ParseOrZero(value);
    }

    partial void OnSurveyUseFactor_UChanged(double value)
    {
        SurveyUseFactor_U_Text = value.ToString("0.####", CultureInfo.CurrentCulture);
        NotifySurveyChanged();
    }

    partial void OnSurveyOccupancy_T_TextChanged(string value)
    {
        SurveyOccupancy_T = ParseOrZero(value);
    }

    partial void OnSurveyOccupancy_TChanged(double value)
    {
        SurveyOccupancy_T_Text = value.ToString("0.####", CultureInfo.CurrentCulture);
        NotifySurveyChanged();
    }

    partial void OnSurveyControlledAreaChanged(bool value)
    {
        NotifySurveyChanged();
    }

    public void StraightenSelectedBarrier()
    {
        var b = SelectedBarrier;
        if (b is null) return;
        if (!b.IsDrawable) return;

        double dx = b.X2 - b.X1;
        double dy = b.Y2 - b.Y1;

        double midX = (b.X1 + b.X2) / 2.0;
        double midY = (b.Y1 + b.Y2) / 2.0;

        double L = Math.Sqrt(dx * dx + dy * dy);
        if (L < 1e-6) return;

        bool makeHorizontal = Math.Abs(dx) >= Math.Abs(dy);

        if (makeHorizontal)
        {
            b.X1 = midX - L / 2.0;
            b.Y1 = midY;
            b.X2 = midX + L / 2.0;
            b.Y2 = midY;
        }
        else
        {
            b.X1 = midX;
            b.Y1 = midY - L / 2.0;
            b.X2 = midX;
            b.Y2 = midY + L / 2.0;
        }

        UpdateCanvasExtent();
        RecomputeDistancesForAll();
    }

    public void NudgeSelectedBarrier(double dx, double dy)
    {
        var b = SelectedBarrier;
        if (b is null) return;
        if (!b.IsDrawable) return;

        b.X1 += dx; b.Y1 += dy;
        b.X2 += dx; b.Y2 += dy;

        UpdateCanvasExtent();
        RecomputeDistancesForAll();
    }

    public bool IsXrayRoom => SelectedRoomCategory is
    "Radiography" or "Dedicated Chest Radiography" or "Rad Tube (R&F room)" or "Fluoro Tube (R&F room)"
    or "C-arm" or "Mammography" or "Cardiac Angio" or "Peripheral Angio" or "CT";

    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private string? currentProjectPath;

    private bool _suspendDirty;
    public bool IsLoadingProject { get; private set; }
    public void MarkDirty()
    {
        if (_suspendDirty) return;
        IsDirty = true;
    }

    public void ClearDirty()
    {
        IsDirty = false;
    }

    private void RaiseCtDlpWeekChanged()
    {
        OnPropertyChanged(nameof(CtDlpWeek_Total));
        OnPropertyChanged(nameof(CtHasDlpWeek));
    }

    public void BeginProjectLoad()
    {
        IsLoadingProject = true;
        _suspendDirty = true;
    }

    public void EndProjectLoad()
    {
        IsLoadingProject = false;
        OnPropertyChanged(nameof(IsCT));
        OnPropertyChanged(nameof(IsPET));
        OnPropertyChanged(nameof(IsXrayRoom));
        RaiseCtDlpWeekChanged();
        _suspendDirty = false;
        ClearDirty();
    }

    private void Barriers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (BarrierItemViewModel b in e.OldItems)
                UnhookBarrierDirty(b);

        if (e.NewItems != null)
            foreach (BarrierItemViewModel b in e.NewItems)
                HookBarrierDirty(b);

        MarkDirty();
    }

    private void HookBarrierDirty(BarrierItemViewModel b)
    {
        b.PropertyChanged += Barrier_PropertyChanged;
        b.PreexistingLayers.CollectionChanged += PreexistingLayers_CollectionChanged;

        foreach (var layer in b.PreexistingLayers)
            layer.PropertyChanged += PreLayer_PropertyChanged;
    }

    private void UnhookBarrierDirty(BarrierItemViewModel b)
    {
        b.PropertyChanged -= Barrier_PropertyChanged;
        b.PreexistingLayers.CollectionChanged -= PreexistingLayers_CollectionChanged;

        foreach (var layer in b.PreexistingLayers)
            layer.PropertyChanged -= PreLayer_PropertyChanged;
    }

    private void Barrier_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suspendDirty) return;
        if (e.PropertyName is null) { MarkDirty(); return; }

        // Ignore derived UI outputs (computed)
        if (e.PropertyName is
            nameof(BarrierItemViewModel.TraceText) or
            nameof(BarrierItemViewModel.RequiredThickness_mm) or
            nameof(BarrierItemViewModel.LegendBrush) or
            nameof(BarrierItemViewModel.LegendText) or
            nameof(BarrierItemViewModel.KSource) or
            nameof(BarrierItemViewModel.DisplayName) or
            nameof(BarrierItemViewModel.CommercialGaugeOnly) or
            nameof(BarrierItemViewModel.GaugeKey) or
            nameof(BarrierItemViewModel.ThicknessShortDisplay) or
            nameof(BarrierItemViewModel.EffectiveDistance_m) or
            nameof(BarrierItemViewModel.ExtraOffset_m))
            return;

        MarkDirty();
    }

    private void PreexistingLayers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suspendDirty) return;

        if (e.OldItems != null)
            foreach (PreexistingLayerViewModel layer in e.OldItems)
                layer.PropertyChanged -= PreLayer_PropertyChanged;

        if (e.NewItems != null)
            foreach (PreexistingLayerViewModel layer in e.NewItems)
                layer.PropertyChanged += PreLayer_PropertyChanged;

        MarkDirty();
        if (SelectedBarrier != null)
            ComputeBarrier(SelectedBarrier);
    }

    private void PreLayer_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suspendDirty) return;
        MarkDirty();
    }

    private void CacheTraceToBarrier(BarrierItemViewModel b)
    {
        if (b == null) return;

        b.CachedTraceItems.Clear();
        foreach (var kv in TraceItems)
            b.CachedTraceItems.Add(kv);

        b.CachedResultSummary = ResultSummary ?? "";
    }

    private void ShowCachedTrace(BarrierItemViewModel? b)
    {
        TraceItems.Clear();

        if (b == null)
        {
            ResultSummary = "";
            return;
        }

        if (b.CachedTraceItems.Count == 0)
        {
            ResultSummary = $"{BareBarrierTitle(b)}: (not computed yet)";
            TraceItems.Add(new("Hint", "Click 'Compute this' once to populate trace."));
            return;
        }

        foreach (var kv in b.CachedTraceItems)
            TraceItems.Add(kv);

        ResultSummary = b.CachedResultSummary;
    }

    public ObservableCollection<OccupancySpec> NcrpOccupancyRows { get; } = new();
    public ObservableCollection<PrimaryAirKermaSpec> NcrpPrimaryAirKermaRows { get; } = new();
    public ObservableCollection<SecondaryAirKermaSpec> NcrpSecondaryAirKermaRows { get; } = new();
    public sealed class CoeffRow
    {
        public string Scenario { get; init; } = "";

        public double LeadA { get; init; }
        public double LeadB { get; init; }
        public double LeadG { get; init; }
        public double ConcA { get; init; }
        public double ConcB { get; init; }
        public double ConcG { get; init; }
        public double SteelA { get; init; }
        public double SteelB { get; init; }
        public double SteelG { get; init; }
        public double WoodA { get; init; }
        public double WoodB { get; init; }
        public double WoodG { get; init; }
        public double GypsA { get; init; }
        public double GypsB { get; init; }
        public double GypsG { get; init; }
    }

    public sealed class CTCoeffRow
    {
        public int kVp { get; init; }

        public double LeadA { get; init; }
        public double LeadB { get; init; }
        public double LeadG { get; init; }
        public double ConcA { get; init; }
        public double ConcB { get; init; }
        public double ConcG { get; init; }
        public double SteelA { get; init; }
        public double SteelB { get; init; }
        public double SteelG { get; init; }
        public double GypsA { get; init; }
        public double GypsB { get; init; }
        public double GypsG { get; init; }
    }
    public IReadOnlyList<CoeffRow> PrimaryCoeffTable { get; private set; } = Array.Empty<CoeffRow>();
    public IReadOnlyList<CoeffRow> SecondaryCoeffTable { get; private set; } = Array.Empty<CoeffRow>();
    public IReadOnlyList<CTCoeffRow> CTCoeffTable { get; private set; } = Array.Empty<CTCoeffRow>();
    private static IReadOnlyList<CoeffRow> BuildCoeffTable(IEnumerable<CoeffSpec> coeffs)
    {
        var order = coeffs
    .Select(c => c.Scenario)
    .Distinct()
    .ToList();

        return coeffs
            .GroupBy(c => c.Scenario)
            .OrderBy(g => order.IndexOf(g.Key))
            .Select(g =>
            {
                (double a, double b, double gg) Get(string mat)
                {
                    var hit = g.FirstOrDefault(x => x.MaterialName.Equals(mat, StringComparison.OrdinalIgnoreCase));
                    return hit is null ? (0, 0, 0) : (hit.Alpha, hit.Beta, hit.Gamma);
                }

                var lead = Get("Lead");
                var conc = Get("Concrete");
                var steel = Get("Steel");
                var wood = Get("Wood");
                var gyps = Get("Gypsum Wallboard");

                return new CoeffRow
                {
                    Scenario = g.Key,

                    LeadA = lead.a,
                    LeadB = lead.b,
                    LeadG = lead.gg,
                    ConcA = conc.a,
                    ConcB = conc.b,
                    ConcG = conc.gg,
                    SteelA = steel.a,
                    SteelB = steel.b,
                    SteelG = steel.gg,
                    WoodA = wood.a,
                    WoodB = wood.b,
                    WoodG = wood.gg,
                    GypsA = gyps.a,
                    GypsB = gyps.b,
                    GypsG = gyps.gg,
                };
            })
            .ToList();
    }

    private static IReadOnlyList<CTCoeffRow> BuildCTCoeffTable(IEnumerable<CTCoeffSpec> coeffs)
    {
        var order = coeffs
    .Select(c => c.kVp)
    .Distinct()
    .ToList();

        return coeffs
            .GroupBy(c => c.kVp)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                (double a, double b, double gg) Get(string mat)
                {
                    var hit = g.FirstOrDefault(x => x.MaterialName.Equals(mat, StringComparison.OrdinalIgnoreCase));
                    return hit is null ? (0, 0, 0) : (hit.Alpha, hit.Beta, hit.Gamma);
                }

                var lead = Get("Lead");
                var conc = Get("Concrete");
                var steel = Get("Steel");
               
                var gyps = Get("Gypsum Wallboard");

                return new CTCoeffRow
                {
                    kVp = g.Key,

                    LeadA = lead.a,
                    LeadB = lead.b,
                    LeadG = lead.gg,
                    ConcA = conc.a,
                    ConcB = conc.b,
                    ConcG = conc.gg,
                    SteelA = steel.a,
                    SteelB = steel.b,
                    SteelG = steel.gg,
                   
                    GypsA = gyps.a,
                    GypsB = gyps.b,
                    GypsG = gyps.gg,
                };
            })
            .ToList();
    }
    public sealed class CtProcedureDefaultRow
    {
        public string Procedure { get; set; } = "";
        public double CTDIvol_mGy { get; set; }
        public double ScanLength_cm { get; set; }
        public double DLP_mGy_cm { get; set; }
    }
    public ObservableCollection<CtProcedureDefaultRow> CtProcedureDefaults { get; } = new();

    [ObservableProperty] private string institutionName = "";
    [ObservableProperty] private string departmentLine1 = "";
    [ObservableProperty] private string departmentLine2 = "";
    [ObservableProperty] private string addressLine1 = "";
    [ObservableProperty] private string addressLine2 = "";
    [ObservableProperty] private string phoneLine = "";

    [ObservableProperty] private string facilityName = "";
    [ObservableProperty] private string facilityAddress = "";

    [ObservableProperty] private string physicistName = "";
    [ObservableProperty] private string physicistCredentials = "";
    [ObservableProperty] private string physicistTitle = "";
    [ObservableProperty] private string licenseLine = "";

    [ObservableProperty] private System.Windows.Media.ImageSource? signatureImage;

    /// <summary>
    /// ////////////////////////////////////////////////////////////////
    /// </summary>
    private sealed class ActionOnDispose : IDisposable
    {
        private readonly Action _a;
        public ActionOnDispose(Action a) => _a = a;
        public void Dispose() => _a();
    }

    private IDisposable SuspendDirty()
    {
        var prev = _suspendDirty;
        _suspendDirty = true;
        return new ActionOnDispose(() => _suspendDirty = prev);
    }
}