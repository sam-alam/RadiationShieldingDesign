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
        SpectProtocols.Tc2DayPerVisit => "Tc-99m per visit (mCi)",
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

    public double PatientsPerWeek => Parse(PatientsPerWeekText);

    public SpectStudy ToStudy() => new()
    {
        Protocol = Protocol,
        Activity1_MBq = Parse(Activity1Text) * MBqPerMCi,
        Activity2_MBq = HasActivity2 ? Parse(Activity2Text) * MBqPerMCi : 0,
        Wait_min = Parse(WaitText),
        Scan_min = Parse(ScanText),
        Gap_min = Parse(GapText),
        Stress_min = Parse(StressText),
        PostStressWait_min = Parse(PostStressWaitText),
    };

    /// <summary>One line for the trace and the report.</summary>
    public string Describe()
    {
        var parts = new List<string> { $"{Fmt(PatientsPerWeek)} pts/wk", $"{Activity1Label.Replace(" (mCi)", "")} {Activity1Text} mCi" };
        if (HasActivity2) parts.Add($"stress {Activity2Text} mCi");
        if (HasWait) parts.Add($"{WaitLabel.Replace(" (min)", "").ToLowerInvariant()} {WaitText} min");
        parts.Add($"scan {ScanText} min");
        if (HasStress) parts.Add($"stress {StressText} min, post-stress wait {PostStressWaitText} min");
        if (HasGap) parts.Add($"{GapLabel.Replace(" (min)", "").ToLowerInvariant()} {GapText} min");
        return string.Join(", ", parts);
    }

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);

    private static double Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var t = s.Trim();
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out var v)) return v;
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
        return 0;
    }
}
