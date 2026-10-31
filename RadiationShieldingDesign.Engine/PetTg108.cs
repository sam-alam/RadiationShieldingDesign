namespace RadiationShieldingDesign.Engine
{
    public enum PetIsotope { F18, C11, Ga68 }

    public static class PetTg108
    {
        // Half-lives (minutes)
        public static double HalfLife_min(PetIsotope iso) => iso switch
        {
            PetIsotope.F18 => 109.8,
            PetIsotope.C11 => 20.4,
            PetIsotope.Ga68 => 68.3,
            _ => 109.8
        };

        // TG-108 recommended patient-attenuated dose constant (µSv·m² / (MBq·h))
        public static double GammaPatient_uSv_m2_per_MBq_h(PetIsotope iso, double kPatient_uSv_m2_per_MBq_h)
        {
            const double gammaBareF18 = 0.143;
            double gammaPatientF18 = kPatient_uSv_m2_per_MBq_h;// 0.092;

            double gammaBare = GammaBare_uSv_m2_per_MBq_h(iso);

            // Scale non–F18 isotopes proportionally
            return gammaPatientF18 * (gammaBare / gammaBareF18);
        }


        // TG-108 Table II: effective dose equivalent constant Γ (µSv·m² / (MBq·h))
        public static double GammaBare_uSv_m2_per_MBq_h(PetIsotope iso) => iso switch
        {
            PetIsotope.C11 => 0.148,
            PetIsotope.F18 => 0.143,
            PetIsotope.Ga68 => 0.134,
            _ => 0.143
        };

        // (1 - e^{-λt}) / (λt)
        private static double DecayAveragingFactor(double lambda_per_h, double t_h)
        {
            if (!(lambda_per_h > 0) || !(t_h > 0)) return 1.0;
            return (1.0 - Math.Exp(-lambda_per_h * t_h)) / (lambda_per_h * t_h);
        }

        // K1 (mGy/pt @ 1 m) for Uptake room
        public static double K1_Uptake_mGy_perPt_at1m(double kPatient_uSv_m2_per_MBq_h,
    PetIsotope iso,
    double A0_MBq,
    double uptake_min)
        {
            if (A0_MBq <= 0 || uptake_min <= 0) return 0.0;

            double gamma = GammaPatient_uSv_m2_per_MBq_h(iso, kPatient_uSv_m2_per_MBq_h);
            double tU_h = uptake_min / 60.0;

            double T12_h = HalfLife_min(iso) / 60.0;
            double lambda = Math.Log(2.0) / T12_h;

            double RtU = DecayAveragingFactor(lambda, tU_h);

            // TG-108 Eq. (2)
            double dose_uSv_at1m = gamma * A0_MBq * tU_h * RtU;

            return dose_uSv_at1m / 1000.0;
        }


        // K1 (mGy/pt @ 1 m) for PET room (imaging), after uptake decay & optional voiding factor
        public static double K1_PetRoom_mGy_perPt_at1m(double kPatient_uSv_m2_per_MBq_h,
    PetIsotope iso,
    double A0_MBq,
    double uptake_min,
    double imaging_min,
    double voidingFactor = 0.85) // TG-108 default
        {
            if (A0_MBq <= 0 || imaging_min <= 0) return 0.0;

            double gamma = GammaPatient_uSv_m2_per_MBq_h(iso, kPatient_uSv_m2_per_MBq_h);

            double tU_h = uptake_min / 60.0;
            double tI_h = imaging_min / 60.0;

            double T12_h = HalfLife_min(iso) / 60.0;
            double lambda = Math.Log(2.0) / T12_h;

            double FU = Math.Exp(-lambda * tU_h);   // uptake decay
            double RtI = DecayAveragingFactor(lambda, tI_h);

            double Aeff = A0_MBq * FU * Math.Clamp(voidingFactor, 0, 1);

            // TG-108 Eq. (9)
            double dose_uSv_at1m = gamma * Aeff * tI_h * RtI;

            return dose_uSv_at1m / 1000.0;
        }

    }
}
