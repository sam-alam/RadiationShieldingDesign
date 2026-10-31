//using RadiationShieldingDesign.Engine;
//using System;
//using System.Collections.Specialized;
using System.Globalization;
using System.IO;
//using System.Linq;
//using System.Text.Json;
//using System.Text.Json.Serialization;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;

namespace RadiationShieldingDesign;

public static class ProjectIO
{
    //private static readonly JsonSerializerOptions JsonOpts = new()
    //{
    //    WriteIndented = true,
    //    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    //};

    public static void Save(MainViewModel vm, string path)
        {
            var dto = ToDto(vm);

            var json = System.Text.Json.JsonSerializer.Serialize(dto,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(path, json);

            // update VM bookkeeping
            vm.CurrentProjectPath = path;
            vm.ClearDirty();
        }

    public static void LoadInto(MainViewModel vm, string path)
    {
        var json = File.ReadAllText(path);
        var dto = System.Text.Json.JsonSerializer.Deserialize<ProjectDto>(json)
                  ?? throw new InvalidDataException("Invalid project file.");

        vm.BeginProjectLoad();
        try
        {
            vm.Barriers.Clear();
            vm.SelectedBarrier = null;

            // --------- Apply project fields (while IsLoadingProject=true) ----------
            vm.SelectedRoomCategory = dto.SelectedRoomCategory;
            //vm.IsCT = string.Equals(vm.SelectedRoomCategory, "CT", StringComparison.OrdinalIgnoreCase);
            vm.RoomName = dto.RoomName ?? "";
            vm.HasActiveDesign = dto.HasActiveDesign;

            var rp = dto.ReportProfile ?? new ReportProfileDto();

            vm.InstitutionName = rp.InstitutionName ?? "";
            vm.DepartmentLine1 = rp.DepartmentLine1 ?? "";
            vm.DepartmentLine2 = rp.DepartmentLine2 ?? "";
            vm.AddressLine1 = rp.AddressLine1 ?? "";
            vm.AddressLine2 = rp.AddressLine2 ?? "";
            vm.PhoneLine = rp.PhoneLine ?? "";

            vm.FacilityName = rp.FacilityName ?? "";
            vm.FacilityAddress = rp.FacilityAddress ?? "";

            vm.PhysicistName = rp.PhysicistName ?? "";
            vm.PhysicistCredentials = rp.PhysicistCredentials ?? "";
            vm.PhysicistTitle = rp.PhysicistTitle ?? "";
            vm.LicenseLine = rp.LicenseLine ?? "";

            if (!string.IsNullOrWhiteSpace(rp.SignaturePngBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(rp.SignaturePngBase64);
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bi.StreamSource = new MemoryStream(bytes);
                    bi.EndInit();
                    bi.Freeze();
                    vm.SignatureImage = bi;
                }
                catch { vm.SignatureImage = null; }
            }
            else vm.SignatureImage = null;

            vm.CanvasWidth = dto.CanvasWidth;
            vm.CanvasHeight = dto.CanvasHeight;
            vm.CmPerPixel = dto.CmPerPixel;
            vm.TubeX = dto.TubeX;
            vm.TubeY = dto.TubeY;

            ApplyBackground(vm,
            dto.BackgroundPngBase64,
            dto.BackgroundImagePath,
            dto.BackgroundOpacity, dto.BackgroundScale,
            dto.BackgroundOffsetX, dto.BackgroundOffsetY);

            vm.PatientsPerWeek = dto.PatientsPerWeek;
            vm.RoomHeight_ft = dto.RoomHeight_ft;
            vm.LevelBelowHeight_ft = dto.LevelBelowHeight_ft;
            vm.ChestBuckyToWall_ft = dto.ChestBuckyToWall_ft;
            vm.SourceToFloor_ft = dto.SourceToFloor_ft;
            vm.SourceToCeiling_ft = dto.SourceToCeiling_ft;

            vm.UseNcrpAverage = dto.UseNcrpAverage;
            vm.UseNcrpBusy = dto.UseNcrpBusy;
            vm.UseNcrpWorkloadNormalization = dto.UseNcrpWorkloadNormalization;

            // CT
            vm.CtKvp = dto.CtKvp ?? "120";
            vm.CtIsoCenterHeight_ft = dto.CtIsoCenterHeight_ft;
            vm.CtDlpHeadPerPatient_mGycm = dto.CtDlpHeadPerPatient_mGycm;
            vm.CtDlpBodyPerPatient_mGycm = dto.CtDlpBodyPerPatient_mGycm;
            vm.CtHeadScansPerWeek = dto.CtHeadScansPerWeek;
            vm.CtBodyScansPerWeek = dto.CtBodyScansPerWeek;
            vm.CtUseNcrpCoeffs = dto.CtUseNcrpCoeffs;
            vm.CtMode = dto.CtMode;

            vm.CtApplyContrastWeight = dto.CtApplyContrastWeight;
            vm.CtContrastPercent = dto.CtContrastPercent;

            vm.CtContrastPercentText = dto.CtContrastPercent > 0
                ? dto.CtContrastPercent.ToString("0.####", CultureInfo.CurrentCulture)
                : "0";

            vm.UserMasVal = dto.UserMasVal;
            vm.UserMaVal = dto.UserMaVal;
            vm.UserFluoroTimeMinVal = dto.UserFluoroTimeMinVal;
            vm.UserDapGyCm2Val = dto.UserDapGyCm2Val;

            vm.UserMasText = dto.UserMasVal > 0
    ? dto.UserMasVal.ToString("0.####", CultureInfo.CurrentCulture)
    : "";

            vm.UserMaText = dto.UserMaVal > 0
                ? dto.UserMaVal.ToString("0.####", CultureInfo.CurrentCulture)
                : "";

            vm.UserFluoroTimeMinText = dto.UserFluoroTimeMinVal > 0
                ? dto.UserFluoroTimeMinVal.ToString("0.####", CultureInfo.CurrentCulture)
                : "";

            vm.UserDapGyCm2Text = dto.UserDapGyCm2Val > 0
                ? dto.UserDapGyCm2Val.ToString("0.####", CultureInfo.CurrentCulture)
                : "";

            // PET
            vm.SelectedIsotope = dto.SelectedIsotope;
            vm.PetAdmin_MBq = dto.PetAdmin_MBq;
            vm.PetUptake_min = dto.PetUptake_min;
            vm.PetImaging_min = dto.PetImaging_min;
            vm.PetVoidingFactor = dto.PetVoidingFactor;
            vm.PetPatientDoseConstManual = dto.PetPatientDoseConstManual;
            vm.PetPatientDoseConst_uSv_m2_per_MBq_h = dto.PetPatientDoseConst_uSv_m2_per_MBq_h;

            // Survey
            vm.SurveyRate_mR_per_h = dto.SurveyRate_mR_per_h;
            vm.SurveyOnTime_h_per_week = dto.SurveyOnTime_h_per_week;
            vm.SurveyPerExam_mR = dto.SurveyPerExam_mR;
            vm.SurveyExams_per_week = dto.SurveyExams_per_week;
            vm.SurveyUseFactor_U = dto.SurveyUseFactor_U;
            vm.SurveyOccupancy_T = dto.SurveyOccupancy_T;
            vm.SurveyControlledArea = dto.SurveyControlledArea;


            // --------- Recreate barriers (with stable IDs) ----------
            foreach (var bd in dto.Barriers)
            {
                var b = FromDto(vm, bd);
                vm.Barriers.Add(b);
            }

            // Set delegates and refresh label positions
            foreach (var b in vm.Barriers)
            {
                b.GetCanvasDistance_m = vm.ComputeCanvasDistance_m;
                b.InitDisplayName();
                b.RefreshLabelPosition();
            }

            // Select something
            vm.SelectedBarrier =
    dto.SelectedBarrierId is Guid sid
        ? vm.Barriers.FirstOrDefault(b => b.Id == sid) ?? vm.Barriers.FirstOrDefault()
        : vm.Barriers.FirstOrDefault();

            // Recompute distances and compute results so UI shows thickness immediately
            vm.RecomputeDistancesForAll();
            foreach (var b in vm.Barriers)
                vm.RecomputeBarrier(b);

            vm.UpdateCanvasExtent();

            vm.CurrentProjectPath = path;
            // Force re-evaluation of IsCT / IsPET / etc after category load
            // Room type: these are derived from SelectedRoomCategory (IsCT/IsPET are read-only)
            if (!string.IsNullOrWhiteSpace(dto.SelectedRoomCategory))
            {
                vm.SelectedRoomCategory = dto.SelectedRoomCategory; // triggers your OnSelectedRoomCategoryChanged logic
            }


        }
        finally
        {
            vm.ComputeAllCommand.Execute(null);
            vm.EndProjectLoad(); // clears dirty too
        }
    }

    private const int BackgroundDownsampleThresholdPx = 3200;
    private const int BackgroundDownsampleMaxPx = 2400;

    private static System.Windows.Media.Imaging.BitmapSource LoadBackgroundBitmapFromBytes(
        byte[] bytes,
        out int srcW, out int srcH,
        out int decW, out int decH,
        out double scaleCompensation)
    {
        // read original size without full decode
        using (var msInfo = new MemoryStream(bytes))
        {
            var decInfo = System.Windows.Media.Imaging.BitmapDecoder.Create(
                msInfo,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.None);

            var frame = decInfo.Frames[0];
            srcW = frame.PixelWidth;
            srcH = frame.PixelHeight;
        }

        int maxDim = Math.Max(srcW, srcH);

        int? decodePixelWidth = null;
        int? decodePixelHeight = null;

        if (maxDim > BackgroundDownsampleThresholdPx)
        {
            if (srcW >= srcH)
                decodePixelWidth = BackgroundDownsampleMaxPx;
            else
                decodePixelHeight = BackgroundDownsampleMaxPx;
        }

        // decode (possibly downsampled)
        var bi = new System.Windows.Media.Imaging.BitmapImage();
        bi.BeginInit();
        bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bi.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat;
        bi.StreamSource = new MemoryStream(bytes);

        if (decodePixelWidth.HasValue) bi.DecodePixelWidth = decodePixelWidth.Value;
        if (decodePixelHeight.HasValue) bi.DecodePixelHeight = decodePixelHeight.Value;

        bi.EndInit();
        bi.Freeze();

        decW = bi.PixelWidth;
        decH = bi.PixelHeight;

        scaleCompensation = (decW > 0 && decH > 0)
            ? (double)Math.Max(srcW, srcH) / Math.Max(decW, decH)
            : 1.0;

        return bi;
    }

    private static System.Windows.Media.Imaging.BitmapSource LoadBackgroundBitmapFromFile(
        string path,
        out int srcW, out int srcH,
        out int decW, out int decH,
        out double scaleCompensation)
    {
        // read original size
        using (var fsInfo = File.OpenRead(path))
        {
            var decInfo = System.Windows.Media.Imaging.BitmapDecoder.Create(
                fsInfo,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.None);

            var frame = decInfo.Frames[0];
            srcW = frame.PixelWidth;
            srcH = frame.PixelHeight;
        }

        int maxDim = Math.Max(srcW, srcH);

        int? decodePixelWidth = null;
        int? decodePixelHeight = null;

        if (maxDim > BackgroundDownsampleThresholdPx)
        {
            if (srcW >= srcH)
                decodePixelWidth = BackgroundDownsampleMaxPx;
            else
                decodePixelHeight = BackgroundDownsampleMaxPx;
        }

        using var fs = File.OpenRead(path);

        var bi = new System.Windows.Media.Imaging.BitmapImage();
        bi.BeginInit();
        bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bi.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat;
        bi.StreamSource = fs;

        if (decodePixelWidth.HasValue) bi.DecodePixelWidth = decodePixelWidth.Value;
        if (decodePixelHeight.HasValue) bi.DecodePixelHeight = decodePixelHeight.Value;

        bi.EndInit();
        bi.Freeze();

        decW = bi.PixelWidth;
        decH = bi.PixelHeight;

        scaleCompensation = (decW > 0 && decH > 0)
            ? (double)Math.Max(srcW, srcH) / Math.Max(decW, decH)
            : 1.0;

        return bi;
    }

    static void ApplyBackground(MainViewModel vm,
    string? b64,
    string? path,
    double opacity, double scale, double offX, double offY)
    {
        vm.BackgroundOpacity = opacity;
        vm.BackgroundScale = scale;
        vm.BackgroundOffsetX = offX;
        vm.BackgroundOffsetY = offY;

        System.Windows.Media.Imaging.BitmapSource? img = null;
        double scaleComp = 1.0;

        // 1) Prefer embedded base64 (portable)
        if (!string.IsNullOrWhiteSpace(b64))
        {
            try
            {
                var bytes = Convert.FromBase64String(b64);

                int srcW, srcH, decW, decH;
                img = LoadBackgroundBitmapFromBytes(bytes, out srcW, out srcH, out decW, out decH, out scaleComp);
            }
            catch
            {
                img = null;
                scaleComp = 1.0;
                // fall back to path
            }
        }

        // 2) Fall back to path-based load
        if (img == null)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                vm.BackgroundImage = null;
                return;
            }

            int srcW, srcH, decW, decH;
            img = LoadBackgroundBitmapFromFile(path, out srcW, out srcH, out decW, out decH, out scaleComp);
        }

        vm.BackgroundImage = img;

        // Option B: if we downsampled, compensate scale so the background appears the same size
        // (do NOT change offsets; offsets are in canvas units)
        if (scaleComp != 1.0)
        {
            vm.BackgroundScale *= scaleComp;
        }
    }

    private static BarrierItemViewModel FromDto(MainViewModel vm, BarrierDto d)
    {
        var b = new BarrierItemViewModel(vm, d.Id)
        {
            Label = d.Label ?? "",
            Name = d.Name ?? "",
            Kind = d.Kind,
            Designation = d.Designation,

            X1 = d.X1,
            Y1 = d.Y1,
            X2 = d.X2,
            Y2 = d.Y2,
            // load offsets if present
            LabelOffset_px = d.LabelOffset_px ?? 0.0,
            LabelAlong_px = d.LabelAlong_px ?? -10.0,
            LabelX = d.LabelX,
            LabelY = d.LabelY,

            MaterialName = d.MaterialName ?? "Lead",
            UseFactor = d.UseFactor,
            Occupancy = d.Occupancy,
            Zone = d.Zone ?? "Uncontrolled",
            ProtectedArea = d.ProtectedArea ?? "",

            PMGyWeek = d.PMGyWeek,
            POverridden = d.POverridden,
            N = d.N,

            UseNcrpK = d.UseNcrpK,

           // K1ManualOverride = d.K1ManualOverride,
           // K1 = d.K1,

            ApplyPreShield = d.ApplyPreShield,
            PreShieldType = d.PreShieldType ?? "",
            PreShield_mm = d.PreShield_mm,

            DistanceOverride_m = d.DistanceOverride_m,
            SaveDistance = d.SaveDistance,
            SavedRawDistance_m = d.SavedRawDistance_m,
            SnapToSource = d.SnapToSource,

            IsCtTertiary = d.IsCtTertiary,
            TertiaryHeight_m = d.TertiaryHeight_m,
            TertiaryHeightText = d.TertiaryHeight_m.ToString("G", CultureInfo.CurrentCulture),

            ScatterIsoToMeasurement_m = d.ScatterIsoToMeasurement_m,
            ScatterRate_mGyPerHourAt1m = d.ScatterRate_mGyPerHourAt1m,
            ScatterUGyPerMasAt1m = d.ScatterUGyPerMasAt1m,
            ScatterUGyPerGyCm2At1m = d.ScatterUGyPerGyCm2At1m,

            PetRegion = d.PetRegion,

            LinkedSecondaryId = d.LinkedSecondaryId,
            TwinSecondaryId = d.TwinSecondaryId,
            HasLinkedSecondary = d.HasLinkedSecondary,
            HasTwinSecondary = d.HasTwinSecondary,

            IsHiddenOnCanvas = d.IsHiddenOnCanvas,
            IsLabelHidden = d.IsLabelHidden,
        };

        // Backward-compat: if project file didn't have offsets, compute them from saved LabelX/LabelY
        if (d.LabelOffset_px is null && d.LabelAlong_px is null)
        {
            b.SyncLabelOffsetAlongFromXY();
        }

        // Preexisting layers
        b.PreexistingLayers.Clear();
        foreach (var pl in d.PreexistingLayers ?? new())
        {
            b.PreexistingLayers.Add(new PreexistingLayerViewModel
            {
                MaterialName = pl.MaterialName ?? "None",
                ThicknessValue = pl.ThicknessValue,
                ThicknessUnit = pl.ThicknessUnit ?? "mm"
            });
        }

        b.ThicknessRoundingRule = d.ThicknessRoundingRule ?? ThicknessRoundingRule.Auto;
        b.ApplyThicknessRounding();
        // b.K1Text = b.K1.ToString("G", CultureInfo.CurrentCulture);
        b.LoadK1FromDto(d.K1, d.K1ManualOverride);

        return b;
    }

    private static ProjectDto ToDto(MainViewModel vm)
    {
        var dto = new ProjectDto
        {
            SelectedRoomCategory = vm.SelectedRoomCategory,
            RoomName = vm.RoomName,
            HasActiveDesign = vm.HasActiveDesign,

            CanvasWidth = vm.CanvasWidth,
            CanvasHeight = vm.CanvasHeight,
            CmPerPixel = vm.CmPerPixel,
            TubeX = vm.TubeX,
            TubeY = vm.TubeY,

            // store a background path if you have it; if not, leave null
            BackgroundImagePath = null,
            BackgroundOpacity = vm.BackgroundOpacity,
            BackgroundScale = vm.BackgroundScale,
            BackgroundOffsetX = vm.BackgroundOffsetX,
            BackgroundOffsetY = vm.BackgroundOffsetY,
            BackgroundPngBase64 = EncodeImageToPngBase64(vm.BackgroundImage),

            PatientsPerWeek = vm.PatientsPerWeek,
            RoomHeight_ft = vm.RoomHeight_ft,
            LevelBelowHeight_ft = vm.LevelBelowHeight_ft,
            ChestBuckyToWall_ft = vm.ChestBuckyToWall_ft,
            SourceToFloor_ft = vm.SourceToFloor_ft,
            SourceToCeiling_ft = vm.SourceToCeiling_ft,
            SelectedBarrierId = vm.SelectedBarrier?.Id,
            UseNcrpAverage = vm.UseNcrpAverage,
            UseNcrpBusy = vm.UseNcrpBusy,
            UseNcrpWorkloadNormalization = vm.UseNcrpWorkloadNormalization,

            CtKvp = vm.CtKvp,
            CtIsoCenterHeight_ft = vm.CtIsoCenterHeight_ft,
            CtDlpHeadPerPatient_mGycm = vm.CtDlpHeadPerPatient_mGycm,
            CtDlpBodyPerPatient_mGycm = vm.CtDlpBodyPerPatient_mGycm,
            CtHeadScansPerWeek = vm.CtHeadScansPerWeek,
            CtBodyScansPerWeek = vm.CtBodyScansPerWeek,
            CtUseNcrpCoeffs = vm.CtUseNcrpCoeffs,
            CtMode = vm.CtMode,
            UserMasVal = vm.UserMasVal,
            UserMaVal = vm.UserMaVal,
            UserFluoroTimeMinVal = vm.UserFluoroTimeMinVal,
            UserDapGyCm2Val = vm.UserDapGyCm2Val,
            CtApplyContrastWeight = vm.CtApplyContrastWeight,
            CtContrastPercent = vm.CtContrastPercent,

            SelectedIsotope = vm.SelectedIsotope,
            PetAdmin_MBq = vm.PetAdmin_MBq,
            PetUptake_min = vm.PetUptake_min,
            PetImaging_min = vm.PetImaging_min,
            PetVoidingFactor = vm.PetVoidingFactor,
            PetPatientDoseConstManual = vm.PetPatientDoseConstManual,
            PetPatientDoseConst_uSv_m2_per_MBq_h = vm.PetPatientDoseConst_uSv_m2_per_MBq_h,

            SurveyRate_mR_per_h = vm.SurveyRate_mR_per_h,
            SurveyOnTime_h_per_week = vm.SurveyOnTime_h_per_week,
            SurveyPerExam_mR = vm.SurveyPerExam_mR,
            SurveyExams_per_week = vm.SurveyExams_per_week,
            SurveyUseFactor_U = vm.SurveyUseFactor_U,
            SurveyOccupancy_T = vm.SurveyOccupancy_T,
            SurveyControlledArea = vm.SurveyControlledArea,
        };

        var ReportProfile = new ReportProfileDto
        {
            InstitutionName = vm.InstitutionName,
            DepartmentLine1 = vm.DepartmentLine1,
            DepartmentLine2 = vm.DepartmentLine2,
            AddressLine1 = vm.AddressLine1,
            AddressLine2 = vm.AddressLine2,
            PhoneLine = vm.PhoneLine,

            FacilityName = vm.FacilityName,
            FacilityAddress = vm.FacilityAddress,

            PhysicistName = vm.PhysicistName,
            PhysicistCredentials = vm.PhysicistCredentials,
            PhysicistTitle = vm.PhysicistTitle,
            LicenseLine = vm.LicenseLine,

            SignaturePngBase64 = EncodeImageToPngBase64(vm.SignatureImage)
        };

        foreach (var b in vm.Barriers)
        {
            dto.Barriers.Add(new BarrierDto
            {
                Id = b.Id,
                Label = b.Label,
                Name = b.Name,
                Kind = b.Kind,
                Designation = b.Designation,
                ThicknessRoundingRule = b.ThicknessRoundingRule,
                X1 = b.X1,
                Y1 = b.Y1,
                X2 = b.X2,
                Y2 = b.Y2,

                LabelOffset_px = b.LabelOffset_px,
                LabelAlong_px = b.LabelAlong_px,

                LabelX = b.LabelX,
                LabelY = b.LabelY,

                MaterialName = b.MaterialName,
                UseFactor = b.UseFactor,
                Occupancy = b.Occupancy,
                Zone = b.Zone,
                ProtectedArea = b.ProtectedArea,

                PMGyWeek = b.PMGyWeek,
                POverridden = b.POverridden,
                N = b.N,

                UseNcrpK = b.UseNcrpK,

                K1ManualOverride = b.K1ManualOverride,
                K1 = b.K1,

                ApplyPreShield = b.ApplyPreShield,
                PreShieldType = b.PreShieldType,
                PreShield_mm = b.PreShield_mm,

                DistanceOverride_m = b.DistanceOverride_m,
                SaveDistance = b.SaveDistance,
                SavedRawDistance_m = b.SavedRawDistance_m,
                SnapToSource = b.SnapToSource,

                IsCtTertiary = b.IsCtTertiary,
                TertiaryHeight_m = b.TertiaryHeight_m,

                ScatterIsoToMeasurement_m = b.ScatterIsoToMeasurement_m,
                ScatterRate_mGyPerHourAt1m = b.ScatterRate_mGyPerHourAt1m,
                ScatterUGyPerMasAt1m = b.ScatterUGyPerMasAt1m,
                ScatterUGyPerGyCm2At1m = b.ScatterUGyPerGyCm2At1m,

                PetRegion = b.PetRegion,

                LinkedSecondaryId = b.LinkedSecondaryId,
                TwinSecondaryId = b.TwinSecondaryId,
                HasLinkedSecondary = b.HasLinkedSecondary,
                HasTwinSecondary = b.HasTwinSecondary,

                IsHiddenOnCanvas = b.IsHiddenOnCanvas,
                IsLabelHidden = b.IsLabelHidden,

                PreexistingLayers = b.PreexistingLayers.Select(pl => new PreexistingLayerDto
                {
                    MaterialName = pl.MaterialName,
                    ThicknessValue = pl.ThicknessValue,
                    ThicknessUnit = pl.ThicknessUnit
                }).ToList()
            });
        }

        return dto;
    }

    private static string? EncodeImageToPngBase64(System.Windows.Media.ImageSource? src)
    {
        if (src is not System.Windows.Media.Imaging.BitmapSource bmp) return null;

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return Convert.ToBase64String(ms.ToArray());
    }



}
