namespace RadiationShieldingDesign.Engine;

public sealed class SecondaryAirKermaSpec
{
    public string Scenario { get; set; } = "";
    public double Wnorm_mAmin_per_patient { get; set; }
    //public double Field_cm2_at_df { get; set; }
    //public double df_m { get; set; }
    public double Ksec1_mGy_per_patient { get; set; }
}