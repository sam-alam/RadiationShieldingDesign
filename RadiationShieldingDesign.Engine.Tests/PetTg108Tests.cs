namespace RadiationShieldingDesign.Engine.Tests;

public class PetTg108Tests
{
    [Fact]
    public void K1_Uptake_F18_Pinned() =>
        Approx.Equal(0.028335698598355052,
            PetTg108.K1_Uptake_mGy_perPt_at1m(0.092, PetIsotope.F18, 370, 60));

    [Fact]
    public void K1_PetRoom_F18_Pinned() =>
        Approx.Equal(0.0062038624987748306,
            PetTg108.K1_PetRoom_mGy_perPt_at1m(0.092, PetIsotope.F18, 370, 60, 20, 0.85));
}
