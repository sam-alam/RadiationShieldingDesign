namespace RadiationShieldingDesign.Engine;

public sealed class WorkloadSpec
{
    public string RoomType { get; init; } = "";
    public double WorkloadPerPatient_mAmin { get; init; }
    public int Patients_Average { get; init; }
    public int Patients_Busy { get; init; }
    public double Wtot_Average_mAmin_week { get; init; }
    public double Wtot_Busy_mAmin_week { get; init; }
}