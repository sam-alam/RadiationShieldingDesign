namespace RadiationShieldingDesign.Engine;

public sealed class MaterialSpec
{
    public string Name { get; init; } = "";
    public double Density_g_cm3 { get; init; }

    // Exponential-fit parameters used in the NCRP-147 form
    public double Alpha { get; init; }
    public double Beta { get; init; }
    public double Gamma { get; init; }
}