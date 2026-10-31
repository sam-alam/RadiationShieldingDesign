namespace RadiationShieldingDesign.Engine;

public sealed class BarrierCoeffSpec
{
    public string BarrierType { get; init; } = "";
    public string MaterialName { get; init; } = "";
    public double Alpha { get; init; }
    public double Beta { get; init; }
    public double Gamma { get; init; }
}