namespace RadiationShieldingDesign.Engine
{
    public enum PetShieldMaterial
    {
        Lead,
        Concrete,
        Iron,
        //  LeadGlass
    }

    public static class PetAttenuation
    {
        // Tenth-Value Layer (TVL) at 511 keV
        // Lead: 1.37 cm, Concrete: 11.3 cm, Tungsten: 0.89 cm, Lead glass: 2.65 cm
        // Source: AAPM Task Group 108 (Madsen et al., Med Phys 33(1):4-15, 2006), 511 keV shielding data.
        public static double Tvl_cm(PetShieldMaterial mat) => mat switch
        {
            PetShieldMaterial.Iron => 3.8,
            PetShieldMaterial.Lead => 1.37,
            //  PetShieldMaterial.LeadGlass => 2.65,
            PetShieldMaterial.Concrete => 11.3,
            _ => 1.37
        };

        public record ArcherParams(double Alpha, double Beta, double Gamma);

        public static ArcherParams Archer(PetShieldMaterial mat) => mat switch
        {
            PetShieldMaterial.Lead =>
                new ArcherParams(1.543, -0.4408, 2.136),

            PetShieldMaterial.Concrete =>
                new ArcherParams(0.1539, -0.1161, 2.0752),

            PetShieldMaterial.Iron =>
                new ArcherParams(0.5704, -0.3063, 0.6326),

            _ => throw new NotSupportedException("Material not in TG-108")
        };

        // TG-108 Archer inversion: B -> thickness (cm)
        public static double ThicknessFromB_mm(double B, PetShieldMaterial mat)
        {
            B = Math.Clamp(B, 1e-12, 1.0);

            var p = Archer(mat);
            double alpha = p.Alpha, beta = p.Beta, gamma = p.Gamma;


            // --- Use Archer ONLY if parameters are valid ---
            bool hasArcher =
                alpha > 0 &&
                gamma > 0 &&
                (1.0 + beta / alpha) > 0;

            if (hasArcher)
            {
                double betaOverAlpha = beta / alpha;

                // Archer inversion (TG-108)
                // exp(alpha*gamma*x) = (B^{-gamma} + beta/alpha) / (1 + beta/alpha)
                double numerator = Math.Pow(B, -gamma) + betaOverAlpha;
                double denominator = 1.0 + betaOverAlpha;

                if (numerator > 0 && denominator > 0)
                {
                    double x_cm = (1.0 / (alpha * gamma)) *
                                  Math.Log(numerator / denominator);

                    if (double.IsFinite(x_cm) && x_cm >= 0)
                        return x_cm * 10.0; // cm → mm
                }
            }

            // --- Fallback: TVL ---
            double tvl_cm = Tvl_cm(mat);
            double t_cm = tvl_cm * Math.Log10(1.0 / B);
            return Math.Max(0.0, t_cm * 10.0); // cm → mm
        }

        // Archer forward model: thickness (mm) -> transmission B (0..1)
        // Matches your inversion formula:
        //  B^{-gamma} = (1+r) * exp(alpha*gamma*x) - r, where r = beta/alpha and x in cm
        public static double ArcherTransmission(double alpha, double beta, double gamma, double t_mm)
        {
            if (!(alpha > 0) || !(gamma > 0) || !(t_mm >= 0)) return 1.0;

            double x_cm = t_mm / 10.0;
            double r = beta / alpha;

            double inside = (1.0 + r) * Math.Exp(alpha * gamma * x_cm) - r;
            if (!(inside > 0) || double.IsNaN(inside) || double.IsInfinity(inside)) return 1.0;

#if NET5_0_OR_GREATER
            double B = Math.Pow(inside, -1.0 / gamma);
            if (!double.IsFinite(B)) return 1.0;
#else
    double B = Math.Pow(inside, -1.0 / gamma);
    if (double.IsNaN(B) || double.IsInfinity(B)) return 1.0;
#endif

            return Math.Clamp(B, 1e-12, 1.0);
        }


        public static ArcherParams GetArcherParams(PetShieldMaterial mat) => Archer(mat);

        public static double TransmissionFromThickness(double t_mm, PetShieldMaterial mat)
        {
            if (t_mm <= 0) return 1.0;

            var p = GetArcherParams(mat);
            bool hasArcher = p.Alpha > 0 && p.Gamma > 0 && (1.0 + p.Beta / p.Alpha) > 0;

            if (hasArcher)
                return ArcherTransmission(p.Alpha, p.Beta, p.Gamma, t_mm); // your ArcherTransmission logic (PET version)

            // TVL fallback
            double tvl_cm = Tvl_cm(mat);
            double t_cm = t_mm / 10.0;
            double B = Math.Pow(10.0, -t_cm / tvl_cm);
            return Math.Clamp(B, 1e-12, 1.0);
        }


        //public static double ThicknessFromB_mm(double B, PetShieldMaterial mat)
        //    => ThicknessFromB_cm(B, mat) * 10.0;
    }
}
