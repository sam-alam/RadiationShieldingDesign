namespace RadiationShieldingDesign.Engine;

public sealed class BarrierRequest
{
    public string BarrierId { get; init; } = "";
    public string MaterialName { get; init; } = "Lead";
    public double Distance_m { get; init; }
    public double Workload_mAmin_week { get; init; }    // optional for later use
    public double UseFactor { get; init; }
    public double Occupancy { get; init; }
    public double PermissibleP_mGy_week { get; init; }
    public double N { get; init; } = 1.0;
    public double K1 { get; init; }                     // Kp^1 or Ksec^1
    public double PreShield_mm { get; init; }
    public bool IsPrimary { get; init; } = true;
 
}

public sealed class BarrierResult(string id, double x_mm, Dictionary<string, double> trace)
{
    public string BarrierId { get; } = id;
    public double RequiredThickness_mm { get; } = x_mm;
    public Dictionary<string, double> Trace { get; } = trace;
}
