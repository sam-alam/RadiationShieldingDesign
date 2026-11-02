using RadiationShieldingDesign.Engine.Xray;

namespace RadiationShieldingDesign.Engine.Tests.Xray;

public class MultiPositionSolverTests
{
    private static NcrpDataStore Load() =>
        NcrpDataStore.LoadFromBundle(Path.Combine(AppContext.BaseDirectory, "Data", "ncrp147_data_bundle.json"));

    private static XrayComponent Primary(NcrpDataStore s, string scenario, double n, double u, double d, double pre = 0)
    {
        Assert.True(s.TryGetPrimaryK1(scenario, out var k));
        Assert.True(s.TryGetPrimaryCoeff(scenario, "Lead", out var c));
        return new XrayComponent($"{scenario} – primary", true, k, n, u, d, c.a, c.b, c.g, pre);
    }

    private static XrayComponent Secondary(NcrpDataStore s, string scenario, double n, double d)
    {
        Assert.True(s.TryGetSecondaryK1(scenario, out var k));
        Assert.True(s.TryGetSecondaryCoeff(scenario, "Lead", out var c));
        return new XrayComponent($"{scenario} – secondary", false, k, n, 1, d, c.a, c.b, c.g);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.85)]   // chest bucky receptor pre-shield
    public void Single_Primary_Matches_Existing_ShieldingCalculator(double preShield)
    {
        var s = Load();
        const string sc = "Rad Room (chest bucky)";
        var comp = Primary(s, sc, 120, 1.0, 2.1, preShield);

        var multi = MultiPositionSolver.Solve(new MultiPositionRequest
        {
            Occupancy = 1, P_mGy_week = 0.02, Components = { comp }
        });

        var classic = ShieldingCalculator.Compute(new BarrierRequest
        {
            Distance_m = 2.1, UseFactor = 1, Occupancy = 1, PermissibleP_mGy_week = 0.02,
            N = 120, K1 = comp.K1_mGyPerPatient, PreShield_mm = preShield, IsPrimary = true
        }, new MaterialSpec { Alpha = comp.Alpha, Beta = comp.Beta, Gamma = comp.Gamma }, 0.0);

        Assert.True(Math.Abs(multi.RequiredThickness_mm - classic.RequiredThickness_mm) < 0.001,
            $"multi {multi.RequiredThickness_mm} vs classic {classic.RequiredThickness_mm}");
    }

    [Fact]
    public void Single_Secondary_Matches_Existing_ShieldingCalculator()
    {
        var s = Load();
        var comp = Secondary(s, "Rad Room (all barriers)", 120, 3.0);
        var multi = MultiPositionSolver.Solve(new MultiPositionRequest { Occupancy = 0.5, P_mGy_week = 0.02, Components = { comp } });
        var classic = ShieldingCalculator.Compute(new BarrierRequest
        {
            Distance_m = 3.0, UseFactor = 1, Occupancy = 0.5, PermissibleP_mGy_week = 0.02,
            N = 120, K1 = comp.K1_mGyPerPatient, IsPrimary = false
        }, new MaterialSpec { Alpha = comp.Alpha, Beta = comp.Beta, Gamma = comp.Gamma }, 0.0);
        Assert.True(Math.Abs(multi.RequiredThickness_mm - classic.RequiredThickness_mm) < 0.001);
    }

    [Fact]
    public void Chest_Wall_Primary_Plus_Table_Secondary_Needs_More_Than_Primary_Alone()
    {
        var s = Load();
        var prim = Primary(s, "Rad Room (chest bucky)", 120, 1.0, 2.1, 0.85);
        var sec = Secondary(s, "Rad Room (floor or other barriers)", 120, 2.5);

        var alone = MultiPositionSolver.Solve(new MultiPositionRequest { Components = { prim } });
        var both = MultiPositionSolver.Solve(new MultiPositionRequest { Components = { prim, sec } });

        Assert.True(both.RequiredThickness_mm > alone.RequiredThickness_mm);
        Assert.True(both.Week_mGy_AtRequired <= 0.02 * (1 + 1e-9));
        Approx.Equal(1.0, both.Components.Sum(c => c.Fraction));
    }

    [Fact]
    public void Two_Sources_Each_Meeting_P_Alone_Exceed_P_Together()
    {
        // Why R&F can't be designed as two independent rooms on the same wall.
        var s = Load();
        var fluoro = Secondary(s, "Fluoro Tube (R&F room)", 20, 3.0);
        var overhead = Secondary(s, "Rad Tube (R&F room)", 25, 3.0);

        double xF = MultiPositionSolver.Solve(new MultiPositionRequest { Components = { fluoro } }).RequiredThickness_mm;
        double xO = MultiPositionSolver.Solve(new MultiPositionRequest { Components = { overhead } }).RequiredThickness_mm;
        var both = MultiPositionSolver.Solve(new MultiPositionRequest { Components = { fluoro, overhead } });

        Assert.True(both.RequiredThickness_mm > Math.Max(xF, xO));
    }

    [Fact]
    public void No_Shielding_Needed_Gives_Zero_And_Components_Listed()
    {
        var s = Load();
        var sec = Secondary(s, "Rad Room (all barriers)", 10, 10.0);
        var r = MultiPositionSolver.Solve(new MultiPositionRequest { Occupancy = 0.05, Components = { sec } });
        Assert.Equal(0.0, r.RequiredThickness_mm);
        Assert.Single(r.Components);
    }

    [Fact]
    public void Transmission_Is_One_At_Zero_And_Decreasing()
    {
        Assert.Equal(1.0, MultiPositionSolver.Transmission(2.3, 15, 0.5, 0));
        double prev = 1;
        for (double x = 0.1; x < 5; x += 0.1)
        {
            double b = MultiPositionSolver.Transmission(2.3, 15, 0.5, x);
            Assert.True(b < prev);
            prev = b;
        }
    }
}

public class MultiPositionReceptorTests
{
    private static NcrpDataStore Load() =>
        NcrpDataStore.LoadFromBundle(Path.Combine(AppContext.BaseDirectory, "Data", "ncrp147_data_bundle.json"));

    [Fact]
    public void Chest_Wall_Same_Position_Scatter_Through_Receptor_Adds_Little()
    {
        // Chest bucky wall: chest primary (receptor 0.85 mm Pb) + chest scatter at 0.4 m through the receptor
        // + table scatter. Combined stays within a few % of primary alone (NCRP: secondary small on primary barriers).
        var s = Load();
        const string ch = "Rad Room (chest bucky)", tb = "Rad Room (floor or other barriers)";
        s.TryGetPrimaryK1(ch, out var kp); s.TryGetPrimaryCoeff(ch, "Lead", out var cp);
        s.TryGetSecondaryK1(ch, out var ks); s.TryGetSecondaryCoeff(ch, "Lead", out var cs);
        s.TryGetSecondaryK1(tb, out var kt); s.TryGetSecondaryCoeff(tb, "Lead", out var ct);
        var prim = new XrayComponent("chest primary", true, kp, 120, 1, 2.23, cp.a, cp.b, cp.g, 0.85);
        var chestSec = new XrayComponent("chest secondary", false, ks, 120, 1, 0.4, cs.a, cs.b, cs.g, 0.85);
        var tableSec = new XrayComponent("table secondary", false, kt, 120, 1, 2.8, ct.a, ct.b, ct.g);

        double alone = MultiPositionSolver.Solve(new MultiPositionRequest { Components = { prim } }).RequiredThickness_mm;
        double all = MultiPositionSolver.Solve(new MultiPositionRequest { Components = { prim, chestSec, tableSec } }).RequiredThickness_mm;
        Assert.True(all > alone && all < alone * 1.10, $"alone {alone}, combined {all}");
    }
}
