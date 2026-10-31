namespace RadiationShieldingDesign.Engine;

public sealed class PrimaryAirKermaSpec
{
    public string Scenario { get; set; } = "";
    public double Wnorm_mAmin_per_patient { get; set; }
    public double Kp1_mGy_per_patient { get; set; }
    public double TypicalPatientsAverage { get; set; }   // NEW
    public double TypicalPatientsBusy { get; set; }      // NEW
}