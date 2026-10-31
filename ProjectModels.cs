using RadiationShieldingDesign.Engine;
//using System;
//using System.Collections.Generic;

namespace RadiationShieldingDesign;


public sealed class ReportProfileDto
{
    // Letterhead / institution block
    public string? InstitutionName { get; set; }
    public string? DepartmentLine1 { get; set; }
    public string? DepartmentLine2 { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? PhoneLine { get; set; }

    // Report header
    public string? FacilityName { get; set; }
    public string? FacilityAddress { get; set; }

    // Sign-off
    public string? PhysicistName { get; set; }
    public string? PhysicistCredentials { get; set; } // e.g., "PhD, DABR"
    public string? PhysicistTitle { get; set; }
    public string? LicenseLine { get; set; }          // e.g., "NYS License #: ..."
    public string? SignaturePngBase64 { get; set; }   // optional
}

public sealed class ProjectDto
{
    public int Version { get; set; } = 2;
    public bool HasActiveDesign { get; set; }
    // Project identity
    public string? SelectedRoomCategory { get; set; }
    public string? RoomName { get; set; }

    // Canvas
    public double CanvasWidth { get; set; }
    public double CanvasHeight { get; set; }
    public double CmPerPixel { get; set; }
    public double TubeX { get; set; }
    public double TubeY { get; set; }

    // Background (use path OR bytes; path simplest)
    public string? BackgroundPngBase64 { get; set; }
    public string? BackgroundImagePath { get; set; }
    public double BackgroundOpacity { get; set; }
    public double BackgroundScale { get; set; }
    public double BackgroundOffsetX { get; set; }
    public double BackgroundOffsetY { get; set; }

    // Room parameters
    public double PatientsPerWeek { get; set; }
    public double RoomHeight_ft { get; set; }
    public double LevelBelowHeight_ft { get; set; }
    public double ChestBuckyToWall_ft { get; set; }
    public double SourceToFloor_ft { get; set; }
    public double SourceToCeiling_ft { get; set; }
    public Guid? SelectedBarrierId { get; set; }
    // NCRP flags
    public bool UseNcrpAverage { get; set; }
    public bool UseNcrpBusy { get; set; }
    public bool UseNcrpWorkloadNormalization { get; set; }

    // CT
    public string? CtKvp { get; set; }
    public double CtIsoCenterHeight_ft { get; set; }
    public double CtDlpHeadPerPatient_mGycm { get; set; }
    public double CtDlpBodyPerPatient_mGycm { get; set; }
    public double CtHeadScansPerWeek { get; set; }
    public double CtBodyScansPerWeek { get; set; }
    public bool CtUseNcrpCoeffs { get; set; }
    public MainViewModel.CtCalcMode CtMode { get; set; }

    public bool CtApplyContrastWeight { get; set; }
    public double CtContrastPercent { get; set; }

    // CT / fluoro / map workload inputs shown in UI
    public double UserMasVal { get; set; }
    public double UserMaVal { get; set; }
    public double UserFluoroTimeMinVal { get; set; }
    public double UserDapGyCm2Val { get; set; }

    // PET
    public PetIsotope SelectedIsotope { get; set; }
    public double PetAdmin_MBq { get; set; }
    public double PetUptake_min { get; set; }
    public double PetImaging_min { get; set; }
    public double PetVoidingFactor { get; set; }
    public bool PetPatientDoseConstManual { get; set; }
    public double PetPatientDoseConst_uSv_m2_per_MBq_h { get; set; }

    // Survey
    public double SurveyRate_mR_per_h { get; set; }
    public double SurveyOnTime_h_per_week { get; set; }
    public double SurveyPerExam_mR { get; set; }
    public double SurveyExams_per_week { get; set; }
    public double SurveyUseFactor_U { get; set; }
    public double SurveyOccupancy_T { get; set; }
    public bool SurveyControlledArea { get; set; }

    // Barriers
    public List<BarrierDto> Barriers { get; set; } = new();
    public ReportProfileDto ReportProfile { get; set; } = new();

}

public sealed class BarrierDto
{
    public Guid Id { get; set; }
    public ThicknessRoundingRule? ThicknessRoundingRule { get; set; }
    public string? Label { get; set; }
    public string? Name { get; set; }
    public BarrierKind Kind { get; set; }
    public BarrierDesignation Designation { get; set; }

    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }

    public double LabelX { get; set; }
    public double LabelY { get; set; }
    public double? LabelOffset_px { get; set; }
    public double? LabelAlong_px { get; set; }

    public string? MaterialName { get; set; }
    public double UseFactor { get; set; }
    public double Occupancy { get; set; }
    public string? Zone { get; set; }
    public string? ProtectedArea { get; set; }

    public double PMGyWeek { get; set; }
    public bool POverridden { get; set; }
    public double N { get; set; }

    public bool UseNcrpK { get; set; }

    public bool K1ManualOverride { get; set; }
    public double K1 { get; set; }

    public bool ApplyPreShield { get; set; }
    public string? PreShieldType { get; set; }
    public double PreShield_mm { get; set; }

    public double DistanceOverride_m { get; set; }
    public bool SaveDistance { get; set; }
    public double SavedRawDistance_m { get; set; }
    public bool SnapToSource { get; set; }

    public bool IsCtTertiary { get; set; }
    public double TertiaryHeight_m { get; set; }

    public double ScatterIsoToMeasurement_m { get; set; }
    public double ScatterRate_mGyPerHourAt1m { get; set; }
    public double ScatterUGyPerMasAt1m { get; set; }
    public double ScatterUGyPerGyCm2At1m { get; set; }

    public PetSourceRegion PetRegion { get; set; }

    public Guid? LinkedSecondaryId { get; set; }
    public Guid? TwinSecondaryId { get; set; }
    public bool HasLinkedSecondary { get; set; }
    public bool HasTwinSecondary { get; set; }

    public bool IsHiddenOnCanvas { get; set; }
    public bool IsLabelHidden { get; set; }

    public List<PreexistingLayerDto> PreexistingLayers { get; set; } = new();
}

public sealed class PreexistingLayerDto
{
    public string? MaterialName { get; set; }
    public double ThicknessValue { get; set; }
    public string? ThicknessUnit { get; set; } // "mm","cm","in"
}

