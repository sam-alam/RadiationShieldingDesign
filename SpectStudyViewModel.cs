using CommunityToolkit.Mvvm.ComponentModel;
using RadiationShieldingDesign.Engine.Nuclear;
using System.Globalization;

namespace RadiationShieldingDesign;

/// <summary>
/// One cardiac study row in the SPECT Parameters card (e.g. "Tc-99m MPI, 1-day rest/stress").
/// Inputs are text (like the PET card) so users can type decimals freely; activities are in mCi.
/// </summary>
public partial class SpectStudyViewModel : ObservableObject
{
    public const double MBqPerMCi = 37.0;

    public SpectStudyViewModel(string protocol)
    {
        Protocol = protocol;
        var d = SpectProtocols.Defaults(protocol);
        Activity1Text = Fmt(d.Activity1_MBq / MBqPerMCi);
        Activity2Text = Fmt(d.Activity2_MBq / MBqPerMCi);
        WaitText = Fmt(d.Wait_min);
        ScanText = Fmt(d.Scan_min);
        GapText = Fmt(d.Gap_min);
        StressText = Fmt(d.Stress_min);
        PostStressWaitText = Fmt(d.PostStressWait_min);
    }

    public string Protocol { get; }

    /// <summary>Raised when any input changes, so the main view model can recompute.</summary>
    public event Action? InputChanged;

    [ObservableProperty] private bool isEnabled;
    [ObservableProperty] private string patientsPerWeekText = "0";
    [ObservableProperty] private string activity1Text = "";
    [ObservableProperty] private string activity2Text = "";
    [ObservableProperty] private string waitText = "";
    [ObservableProperty] private string scanText = "";
    [ObservableProperty] private string gapText = "";
    [ObservableProperty] private string stressText = "";
    [ObservableProperty] private string postStressWaitText = "";

    partial void OnIsEnabledChanged(bool value) => InputChanged?.Invoke();
    partial void OnPatientsPerWeekTextChanged(string value) => InputChanged?.Invoke();
    partial void OnActivity1TextChanged(string value) => InputChanged?.Invoke();
    partial void OnActivity2TextChanged(string value) => InputChanged?.Invoke();
    partial void OnWaitTextChanged(string value) => InputChanged?.Invoke();
    partial void OnScanTextChanged(string value) => InputChanged?.Invoke();
    partial void OnGapTextChanged(string value) => InputChanged?.Invoke();
    partial void OnStressTextChanged(string value) => InputChanged?.Invoke();
    partial void OnPostStressWaitTextChanged(string value) => InputChanged?.Invoke();

    // ---- Which fields apply to this protocol (drives visibility in the card) ----
    public bool HasActivity2 => Protocol is SpectProtocols.Tc1DayRestStress or SpectProtocols.DualIsotope;
    public bool HasWait => Protocol is not SpectProtocols.Tc2DayPerVisit;
    public bool HasGap => Protocol is SpectProtocols.Tc1DayRestStress or SpectProtocols.DualIsotope or SpectProtocols.I123Mibg;
    public bool HasStress => Protocol is SpectProtocols.Tc1DayRestStress or SpectProtocols.Tc2DayPerVisit or SpectProtocols.DualIsotope;

    public string Activity1Label => Protocol switch
    {
        SpectProtocols.Tc1DayRestStress => "Rest Tc-99m (mCi)",
        SpectProtocols.Tc2DayPerVisit => "Stress-visit Tc-99m (mCi)",
        SpectProtocols.Tc2DayRestVisit => "Rest-visit Tc-99m (mCi)",
        SpectProtocols.DualIsotope => "Rest Tl-201 (mCi)",
        SpectProtocols.I123Mibg => "I-123 (mCi)",
        _ => "Tc-99m (mCi)"
    };

    public string Activity2Label => "Stress Tc-99m (mCi)";

    public string WaitLabel => Protocol switch
    {
        SpectProtocols.TcPyp => "Uptake before scan (min)",
        SpectProtocols.TcMuga => "Wait before scan (min)",
        SpectProtocols.I123Mibg => "Early image at (min)",
        _ => "Rest wait before scan (min)"
    };

    public string GapLabel => Protocol == SpectProtocols.I123Mibg ? "Late image at (min)" : "Rest scan → stress (min)";

    /// <summary>Patients per week; NaN when the text is blank or not a number (never silently 0).</summary>
    public double PatientsPerWeek => Parse(PatientsPerWeekText);

    /// <summary>True when the study is enabled and counted (patients/week &gt; 0). NaN → false; validate first.</summary>
    public bool IsCounted => IsEnabled && PatientsPerWeek > 0;

    /// <summary>Fields that apply to this protocol but are blank, non-numeric or negative. Empty = OK.</summary>
    public List<string> InvalidInputs()
    {
        var errs = new List<string>();
        void Check(string label, string text, bool mustBePositive)
        {
            double v = Parse(text);
            if (double.IsNaN(v)) errs.Add($"{Protocol}: '{label}' is blank or not a number ('{text}').");
            else if (v < 0 || (mustBePositive && v <= 0)) errs.Add($"{Protocol}: '{label}' must be {(mustBePositive ? "> 0" : "≥ 0")} ('{text}').");
        }
        Check("Patients/week", PatientsPerWeekText, false);
        Check(Activity1Label, Activity1Text, true);
        if (HasActivity2) Check(Activity2Label, Activity2Text, true);
        if (HasWait) Check(WaitLabel, WaitText, false);
        Check("Scan (min)", ScanText, false);
        if (HasGap) Check(GapLabel, GapText, false);
        if (HasStress)
        {
            Check("Stress (min)", StressText, false);
            Check("Post-stress wait (min)", PostStressWaitText, false);
        }
        return errs;
    }

    /// <summary>Only applicable fields are read; the rest are 0 so an unused blank field cannot poison the timeline.</summary>
    public SpectStudy ToStudy() => new()
    {
        Protocol = Protocol,
        Activity1_MBq = Parse(Activity1Text) * MBqPerMCi,
        Activity2_MBq = HasActivity2 ? Parse(Activity2Text) * MBqPerMCi : 0,
        Wait_min = HasWait ? Parse(WaitText) : 0,
        Scan_min = Parse(ScanText),
        Gap_min = HasGap ? Parse(GapText) : 0,
        Stress_min = HasStress ? Parse(StressText) : 0,
        PostStressWait_min = HasStress ? Parse(PostStressWaitText) : 0,
    };

    /// <summary>One line for the trace and the report.</summary>
    public string Describe()
    {
        var parts = new List<string> { $"{PatientsPerWeekText} pts/wk", $"{Activity1Label.Replace(" (mCi)", "")} {Activity1Text} mCi" };
        if (HasActivity2) parts.Add($"stress {Activity2Text} mCi");
        if (HasWait) parts.Add($"{WaitLabel.Replace(" (min)", "").ToLowerInvariant()} {WaitText} min");
        parts.Add($"scan {ScanText} min");
        if (HasStress) parts.Add($"stress {StressText} min, post-stress wait {PostStressWaitText} min");
        if (HasGap) parts.Add($"{GapLabel.Replace(" (min)", "").ToLowerInvariant()} {GapText} min");
        return string.Join(", ", parts);
    }

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>Strict: blank or unparseable text gives NaN so it is caught by validation instead of becoming 0.</summary>
    private static double Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return double.NaN;
        var t = s.Trim();
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out var v)) return v;
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
        return double.NaN;
    }
}
