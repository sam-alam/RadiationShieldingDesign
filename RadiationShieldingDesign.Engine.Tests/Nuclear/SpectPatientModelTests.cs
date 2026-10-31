using RadiationShieldingDesign.Engine.Nuclear;

namespace RadiationShieldingDesign.Engine.Tests.Nuclear;

public class SpectPatientModelTests
{
    private static readonly NuclideLibrary Lib = NuclideLibrary.Default;

    [Fact]
    public void Single_Injection_Matches_Hand_Calculation()
    {
        // 370 MBq Tc-99m, patient at 1 m for the first hour.
        // ∫A dt = 370·(1 − e^(−λ·1h))/λ, λ = ln2/6.01 h → 349.4607 MBq·h; × 0.0161 µSv·m²/(MBq·h) = 5.6263 µSv.
        var tl = new PatientTimeline
        {
            Injections = { new("Tc-99m", 370, 0) },
            Segments = { new("Room", 0, 60) }
        };
        var d = SpectPatientModel.Compute(tl, Lib).Single();
        Approx.Equal(5.626316771115381, d.Dose_uSv_at1m);
        Approx.Equal(1.0, d.TimeInRegion_h);
    }

    [Fact]
    public void Analytic_Integral_Matches_Numerical_Integration_With_Voiding_And_Two_Injections()
    {
        var tl = SpectProtocols.Create(SpectProtocols.Tc1DayRestStress);
        tl.Voids.Add(new VoidEvent(80, 0.8));
        var n = Lib.Get("Tc-99m");

        foreach (var s in tl.Segments)
        {
            double exact = SpectPatientModel.ActivityIntegral_MBq_h(tl, n, s.Start_min, s.End_min);
            // Midpoint rule, 0.01-min steps.
            double numeric = 0;
            const double dt = 0.01;
            for (double t = s.Start_min + dt / 2; t < s.End_min; t += dt)
                numeric += SpectPatientModel.ActivityAt(tl, n, t) * dt / 60.0;
            Approx.Equal(exact, numeric, 1e-4);
        }
    }

    [Fact]
    public void Rest_Activity_Still_Counts_During_Stress_Imaging()
    {
        var tl = SpectProtocols.Create(SpectProtocols.Tc1DayRestStress);
        var n = Lib.Get("Tc-99m");
        // At the start of the stress scan (t = 135 min): 1110·e^(−λ·35 min) + 370·e^(−λ·135 min).
        double lam = Math.Log(2) / (6.01 * 60);
        double expected = 1110 * Math.Exp(-lam * 35) + 370 * Math.Exp(-lam * 135);
        Approx.Equal(expected, SpectPatientModel.ActivityAt(tl, n, 135));
    }

    [Fact]
    public void Dual_Isotope_Gives_Separate_Results_Per_Nuclide()
    {
        var doses = SpectPatientModel.Compute(SpectProtocols.Create(SpectProtocols.DualIsotope), Lib);
        Assert.Contains(doses, d => d.NuclideId == "Tl-201" && d.Region == SpectRegions.Camera && d.Dose_uSv_at1m > 0);
        Assert.Contains(doses, d => d.NuclideId == "Tc-99m" && d.Region == SpectRegions.Camera && d.Dose_uSv_at1m > 0);
        // Tc-99m is injected at 55 min, so it contributes nothing before then.
        var tc = Lib.Get("Tc-99m");
        Assert.Equal(0.0, SpectPatientModel.ActivityIntegral_MBq_h(SpectProtocols.Create(SpectProtocols.DualIsotope), tc, 0, 15));
    }

    [Fact]
    public void Peak_Hour_Equals_Total_When_Region_Time_Is_Under_An_Hour()
    {
        var tl = new PatientTimeline
        {
            Injections = { new("Tc-99m", 1110, 0) },
            Segments = { new("Camera", 30, 50) }
        };
        var d = SpectPatientModel.Compute(tl, Lib).Single();
        Approx.Equal(d.Dose_uSv_at1m, d.PeakHourDose_uSv_at1m);
    }

    [Fact]
    public void Peak_Hour_Is_Less_Than_Total_When_Patient_Stays_Longer()
    {
        var tl = new PatientTimeline
        {
            Injections = { new("Tc-99m", 1110, 0) },
            Segments = { new("Waiting", 0, 180) }
        };
        var d = SpectPatientModel.Compute(tl, Lib).Single();
        Assert.True(d.PeakHourDose_uSv_at1m < d.Dose_uSv_at1m);
        // Worst hour is the first one.
        double first = SpectPatientModel.ActivityIntegral_MBq_h(tl, Lib.Get("Tc-99m"), 0, 60) * 0.0161;
        Approx.Equal(first, d.PeakHourDose_uSv_at1m);
    }

    [Fact]
    public void Patient_Attenuation_Factor_Scales_Dose()
    {
        var tl = new PatientTimeline
        {
            Injections = { new("Tc-99m", 370, 0) },
            Segments = { new("Room", 0, 60) },
            PatientAttenuationFactor = 0.5
        };
        Approx.Equal(5.626316771115381 * 0.5, SpectPatientModel.Compute(tl, Lib).Single().Dose_uSv_at1m);
    }

    [Theory]
    [InlineData(SpectProtocols.Tc1DayRestStress)]
    [InlineData(SpectProtocols.Tc2DayPerVisit)]
    [InlineData(SpectProtocols.DualIsotope)]
    [InlineData(SpectProtocols.TcPyp)]
    [InlineData(SpectProtocols.TcMuga)]
    [InlineData(SpectProtocols.I123Mibg)]
    public void Every_Preset_Builds_And_Has_Notes(string name)
    {
        var tl = SpectProtocols.Create(name);
        Assert.True(tl.Segments.Count > 0);
        Assert.True(tl.Notes.Count > 0);
        foreach (var s in tl.Segments) Assert.True(s.End_min > s.Start_min);
    }
}
