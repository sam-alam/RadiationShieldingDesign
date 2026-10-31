namespace RadiationShieldingDesign.Engine;

public sealed class CoeffSpec
{
    public string Scenario { get; init; } = "";
    public string MaterialName { get; init; } = "";
    public double Alpha { get; init; }
    public double Beta { get; init; }
    public double Gamma { get; init; }
}

public sealed class CTCoeffSpec
{
    public int kVp { get; init; }
    public string MaterialName { get; init; } = "";
    public double Alpha { get; init; }
    public double Beta { get; init; }
    public double Gamma { get; init; }
}