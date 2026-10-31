namespace RadiationShieldingDesign.Engine.Tests;

public class PetAttenuationTests
{
    [Theory]
    [InlineData(PetShieldMaterial.Lead, 0.5, 5.309043572942558)]
    [InlineData(PetShieldMaterial.Lead, 0.1, 15.937176918154561)]
    [InlineData(PetShieldMaterial.Lead, 0.01, 30.866255472886657)]
    [InlineData(PetShieldMaterial.Concrete, 0.5, 82.82343307439612)]
    [InlineData(PetShieldMaterial.Concrete, 0.1, 193.37728776870935)]
    [InlineData(PetShieldMaterial.Concrete, 0.01, 343.1905683997222)]
    public void Thickness_Pinned_And_RoundTrips(PetShieldMaterial mat, double b, double expectedMm)
    {
        double t = PetAttenuation.ThicknessFromB_mm(b, mat);
        Approx.Equal(expectedMm, t);
        Approx.Equal(b, PetAttenuation.TransmissionFromThickness(t, mat));
    }
}
