using System.IO;
namespace RadiationShieldingDesign.Engine.Tests;

public class ShieldingCalculatorTests
{
    private static NcrpDataStore Load() =>
        NcrpDataStore.LoadFromBundle(Path.Combine(AppContext.BaseDirectory, "Data", "ncrp147_data_bundle.json"));

    private const string PrimaryScenario = "Rad Room (floor or other barriers)";
    private const string SecondaryScenario = "Rad Room (all barriers)";

    [Fact]
    public void Primary_RadRoom_Lead_PinnedThickness()
    {
        var s = Load();
        Assert.True(s.TryGetPrimaryCoeff(PrimaryScenario, "Lead", out var c));
        Assert.True(s.TryGetPrimaryK1(PrimaryScenario, out var k1));
        var m = new MaterialSpec { Name = "Lead", Alpha = c.a, Beta = c.b, Gamma = c.g };
        var r = new BarrierRequest
        {
            BarrierId = "P", MaterialName = "Lead", Distance_m = 3.0, UseFactor = 1.0, Occupancy = 1.0,
            PermissibleP_mGy_week = 0.02, N = 100, K1 = k1, PreShield_mm = 0, IsPrimary = true
        };
        var res = ShieldingCalculator.Compute(r, m, 0.0);
        Approx.Equal(1.4995911345673547, res.RequiredThickness_mm);
    }

    [Fact]
    public void Secondary_RadRoom_Lead_PinnedThickness()
    {
        var s = Load();
        Assert.True(s.TryGetSecondaryCoeff(SecondaryScenario, "Lead", out var c));
        Assert.True(s.TryGetSecondaryK1(SecondaryScenario, out var k1));
        var m = new MaterialSpec { Name = "Lead", Alpha = c.a, Beta = c.b, Gamma = c.g };
        var r = new BarrierRequest
        {
            BarrierId = "S", MaterialName = "Lead", Distance_m = 3.0, UseFactor = 1.0, Occupancy = 1.0,
            PermissibleP_mGy_week = 0.02, N = 100, K1 = k1, PreShield_mm = 0, IsPrimary = false
        };
        var res = ShieldingCalculator.Compute(r, m, 0.0);
        Approx.Equal(0.4078871542943099, res.RequiredThickness_mm);
    }
}
