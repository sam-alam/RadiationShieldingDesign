namespace RadiationShieldingDesign.Engine;

public sealed class PreShieldSpec
{
    public string Type { get; init; } = "";
    public double Lead_mm { get; init; }
    public double Concrete_mm { get; init; }
    public double Steel_mm { get; init; }
}