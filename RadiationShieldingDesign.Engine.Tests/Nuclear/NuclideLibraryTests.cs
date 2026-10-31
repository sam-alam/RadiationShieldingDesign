using RadiationShieldingDesign.Engine.Nuclear;

namespace RadiationShieldingDesign.Engine.Tests.Nuclear;

public class NuclideLibraryTests
{
    private static readonly NuclideLibrary Lib = NuclideLibrary.Default;

    [Fact]
    public void Loads_All_Seven_Nuclides()
    {
        foreach (var id in new[] { "Tc-99m", "Tl-201", "I-123", "In-111", "Ga-67", "I-131", "Xe-133" })
            Assert.True(Lib.Nuclides.ContainsKey(id), $"missing {id}");
    }

    [Fact]
    public void Kusano_Constants_Copied_Correctly()
    {
        Approx.Equal(16.10, Lib.Get("Tc-99m").DoseRateConst_uSv_m2_per_GBq_h);
        Approx.Equal(11.29, Lib.Get("Tl-201").DoseRateConst_uSv_m2_per_GBq_h);
        Approx.Equal(0.01610, Lib.Get("Tc-99m").DoseRateConst_uSv_m2_per_MBq_h);
    }

    // Unit sanity: each fit's transmission at its own tabulated TVL should be ≈ 0.1.
    // Catches cm/mm mix-ups (which would give ~0.8 or ~1e-10, not 0.1).
    [Theory]
    [InlineData("Tc-99m", "Lead")]
    [InlineData("Tl-201", "Lead")]
    [InlineData("I-123", "Lead")]
    [InlineData("In-111", "Lead")]
    [InlineData("Ga-67", "Lead")]
    [InlineData("I-131", "Lead")]
    [InlineData("Xe-133", "Lead")]
    [InlineData("Tc-99m", "Concrete")]
    [InlineData("Tl-201", "Concrete")]
    [InlineData("I-123", "Concrete")]
    [InlineData("In-111", "Concrete")]
    [InlineData("Ga-67", "Concrete")]
    [InlineData("I-131", "Concrete")]
    [InlineData("Xe-133", "Concrete")]
    [InlineData("Tc-99m", "Gypsum Wallboard")]
    [InlineData("Tc-99m", "Lite Concrete")]
    [InlineData("Tc-99m", "Steel")]
    [InlineData("Tc-99m", "Glass")]
    public void Transmission_At_Tabulated_TVL_Is_About_One_Tenth(string nuclide, string material)
    {
        Assert.True(Lib.Get(nuclide).TryGetMaterial(material, out var mt));
        double tf = mt.Transmission(mt.Tvl_mm);
        Assert.True(tf > 0.08 && tf < 0.12, $"{nuclide}/{material}: TF at TVL = {tf:G4}");
    }

    [Fact]
    public void Tl201_Concrete_Uses_TVL_Model_Because_Published_Fit_Is_Degenerate()
    {
        Assert.True(Lib.Get("Tl-201").TryGetMaterial("Concrete", out var mt));
        Assert.Equal("TVL", mt.Model);
        Approx.Equal(105.0, mt.Tvl_mm);                 // 10.50 cm
        Approx.Equal(0.1, mt.Transmission(105.0));
        Approx.Equal(0.01, mt.Transmission(210.0));
    }

    [Fact]
    public void Concrete_Fit_Converted_From_Per_cm_To_Per_mm()
    {
        Assert.True(Lib.Get("Tc-99m").TryGetMaterial("Concrete", out var mt));
        Approx.Equal(0.02813, mt.Alpha_per_mm);
        Approx.Equal(-0.02349, mt.Beta_per_mm);
        Approx.Equal(0.9653, mt.Gamma);
    }

    [Fact]
    public void I123_Concrete_Flagged_Low_Confidence() =>
        Assert.True(Lib.Get("I-123").Materials["Concrete"].LowConfidence);

    [Fact]
    public void Transmission_Stays_Finite_At_Large_Thickness()
    {
        foreach (var n in Lib.Nuclides.Values)
            foreach (var mt in n.Materials.Values)
            {
                double tf = mt.Transmission(3000);
                Assert.True(double.IsFinite(tf) && tf >= 0 && tf < 1e-3, $"{n.Id}/{mt.Material}: {tf}");
            }
    }

    [Fact]
    public void Transmission_Decreases_Monotonically()
    {
        foreach (var n in Lib.Nuclides.Values)
            foreach (var mt in n.Materials.Values)
            {
                double prev = 1.0;
                for (double x = 0.05; x < 4 * mt.Tvl_mm; x += mt.Tvl_mm / 20)
                {
                    double tf = mt.Transmission(x);
                    Assert.True(tf <= prev + 1e-12, $"{n.Id}/{mt.Material} not monotonic at {x} mm");
                    prev = tf;
                }
            }
    }
}
