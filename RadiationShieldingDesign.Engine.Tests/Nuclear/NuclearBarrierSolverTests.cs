using RadiationShieldingDesign.Engine.Nuclear;

namespace RadiationShieldingDesign.Engine.Tests.Nuclear;

public class NuclearBarrierSolverTests
{
    private static readonly NuclideLibrary Lib = NuclideLibrary.Default;

    private static PatientTimeline OneHourTc(double mbq) => new()
    {
        Name = "test",
        Injections = { new("Tc-99m", mbq, 0) },
        Segments = { new("Room", 0, 60) }
    };

    [Fact]
    public void Single_Nuclide_Matches_Closed_Form_Archer_Inversion()
    {
        // Build a workload giving exactly 0.5 mSv/wk unshielded at the point, P = 0.02 → B = 0.04.
        // Closed-form Tc-99m/lead Archer inversion (Kusano α=2.479, β=−1.093, γ=1.376) gives 1.467365 mm.
        double perPatient_mSv = 5.626316771115381 / 1000.0; // 370 MBq, 1 h, 1 m
        double n = 0.5 / perPatient_mSv;

        var res = NuclearBarrierSolver.Solve(new NuclearBarrierRequest
        {
            Workloads = { new(OneHourTc(370), n) },
            Regions = { new("Room", 1.0) },
            P_week_mSv = 0.02,
            P_hour_mSv = 0,          // weekly only
            Material = "Lead"
        }, Lib);

        Assert.Equal("Weekly", res.Governing);
        Assert.True(Math.Abs(res.RequiredThickness_mm - 1.46736514963526) < 0.001,
            $"got {res.RequiredThickness_mm}");
    }

    [Fact]
    public void Dual_Isotope_Result_Is_The_Smallest_Thickness_That_Meets_The_Goal()
    {
        var res = NuclearBarrierSolver.Solve(new NuclearBarrierRequest
        {
            Workloads = { new(SpectProtocols.Create(SpectProtocols.DualIsotope), 40) },
            Regions = { new(SpectRegions.Camera, 2.0) },
            P_week_mSv = 0.02,
            P_hour_mSv = 0,
            Material = "Lead"
        }, Lib);

        Assert.True(res.RequiredThickness_mm > 0);
        Assert.Equal(2, res.Contributions.Count);
        Assert.True(res.Week_mSv_AtRequired <= 0.02 * (1 + 1e-9));

        // Slightly thinner must fail.
        var thinner = res.Contributions.Sum(c =>
            c.Unshielded_week_mSv * c.PreExistingTransmission *
            Lib.Get(c.NuclideId).Materials["Lead"].Transmission(res.RequiredThickness_mm - 0.01));
        Assert.True(thinner > 0.02);
    }

    [Fact]
    public void Summing_K_Then_Inverting_Once_Would_Give_A_Different_Answer()
    {
        // Documents WHY the solver works per nuclide: Tc-99m and Tl-201 attenuate differently in lead.
        var tc = Lib.Get("Tc-99m").Materials["Lead"];
        var tl = Lib.Get("Tl-201").Materials["Lead"];
        Assert.True(Math.Abs(tc.Transmission(1.0) - tl.Transmission(1.0)) > 0.01);
    }

    [Fact]
    public void No_Shielding_Needed_Returns_Zero()
    {
        var res = NuclearBarrierSolver.Solve(new NuclearBarrierRequest
        {
            Workloads = { new(OneHourTc(370), 1) },
            Regions = { new("Room", 10.0) },
            P_week_mSv = 0.1,
            P_hour_mSv = 0.02,
            Material = "Lead"
        }, Lib);
        Assert.Equal(0.0, res.RequiredThickness_mm);
        Assert.Equal("None", res.Governing);
    }

    [Fact]
    public void Hourly_Limit_Can_Govern()
    {
        // Few patients per week (weekly goal easily met) but several at once next to the wall.
        var res = NuclearBarrierSolver.Solve(new NuclearBarrierRequest
        {
            Workloads = { new(OneHourTc(1110), 2) },
            Regions = { new("Room", 1.0, MaxSimultaneousPatients: 4) },
            Occupancy = 0.05,
            P_week_mSv = 0.02,
            P_hour_mSv = 0.02,
            Material = "Lead"
        }, Lib);
        Assert.Equal("Hourly", res.Governing);
        Assert.True(res.ThicknessForHourly_mm > res.ThicknessForWeekly_mm);
        Assert.True(res.Hour_mSv_AtRequired <= 0.02 * (1 + 1e-9));
    }

    [Fact]
    public void Hourly_Check_Ignores_Occupancy()
    {
        PatientTimeline tl = OneHourTc(1110);
        NuclearBarrierResult Run(double T) => NuclearBarrierSolver.Solve(new NuclearBarrierRequest
        {
            Workloads = { new(tl, 10) },
            Regions = { new("Room", 2.0) },
            Occupancy = T,
            Material = "Lead"
        }, Lib);
        Approx.Equal(Run(1.0).Contributions[0].Unshielded_hour_mSv, Run(0.05).Contributions[0].Unshielded_hour_mSv);
        Approx.Equal(Run(1.0).Contributions[0].Unshielded_week_mSv * 0.05, Run(0.05).Contributions[0].Unshielded_week_mSv);
    }

    [Fact]
    public void Pre_Existing_Layer_Reduces_Required_Thickness()
    {
        NuclearBarrierResult Run(List<ShieldLayer> pre) => NuclearBarrierSolver.Solve(new NuclearBarrierRequest
        {
            Workloads = { new(SpectProtocols.Create(SpectProtocols.Tc1DayRestStress), 60) },
            Regions = { new(SpectRegions.Camera, 2.0) },
            Material = "Lead",
            PreExisting = pre
        }, Lib);

        var bare = Run(new());
        var withGypsum = Run(new() { new("Gypsum Wallboard", 31.75) }); // two layers of 5/8" drywall
        Assert.True(withGypsum.RequiredThickness_mm < bare.RequiredThickness_mm);
        Assert.Empty(withGypsum.Warnings);
    }

    [Fact]
    public void Missing_Data_For_Pre_Existing_Layer_Warns_And_Is_Conservative()
    {
        NuclearBarrierResult Run(List<ShieldLayer> pre) => NuclearBarrierSolver.Solve(new NuclearBarrierRequest
        {
            Workloads = { new(SpectProtocols.Create(SpectProtocols.DualIsotope), 40) },
            Regions = { new(SpectRegions.Camera, 2.0) },
            Material = "Lead",
            PreExisting = pre
        }, Lib);

        var res = Run(new() { new("Gypsum Wallboard", 31.75) });
        Assert.Contains(res.Warnings, w => w.Contains("Tl-201") && w.Contains("Gypsum"));
        var tl = res.Contributions.Single(c => c.NuclideId == "Tl-201");
        Assert.Equal(1.0, tl.PreExistingTransmission); // ignored for Tl-201, not silently credited
    }

    [Fact]
    public void Zero_Activity_Preset_Warns()
    {
        var res = NuclearBarrierSolver.Solve(new NuclearBarrierRequest
        {
            Workloads = { new(SpectProtocols.Create(SpectProtocols.TcPyp), 10) },
            Regions = { new(SpectRegions.Camera, 2.0) },
            Material = "Lead"
        }, Lib);
        Assert.Contains(res.Warnings, w => w.Contains("zero activity"));
    }

    [Fact]
    public void Material_Without_Data_Warns()
    {
        var res = NuclearBarrierSolver.Solve(new NuclearBarrierRequest
        {
            Workloads = { new(SpectProtocols.Create(SpectProtocols.DualIsotope), 40) },
            Regions = { new(SpectRegions.Camera, 1.0) },
            Material = "Gypsum Wallboard" // no Tl-201 data
        }, Lib);
        Assert.Contains(res.Warnings, w => w.Contains("Tl-201") && w.Contains("Gypsum Wallboard"));
    }

    [Fact]
    public void SolveThickness_Handles_Edge_Cases()
    {
        Assert.Equal(0.0, NuclearBarrierSolver.SolveThickness(_ => 0.01, 0.02));
        Assert.True(double.IsPositiveInfinity(NuclearBarrierSolver.SolveThickness(_ => 1.0, 0.02)));
        double x = NuclearBarrierSolver.SolveThickness(t => Math.Exp(-t), 0.1);
        Assert.True(Math.Abs(x - Math.Log(10)) < 1e-3);
    }
}
