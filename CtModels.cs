namespace RadiationShieldingDesign
{
    public static class CtTertiary
    {
        // heights in meters; dlp_Gycm_wk in Gy·cm/week
        private static double ExpFit(double a, double b, double h_m)
        {
            return b * Math.Exp(a * h_m);
        }

        // Returns tertiary air kerma in µGy/week
        public static double ComputeK_Tertiary_uGy_wk(
            double barrierHeight_m,
            double ceilingHeight_m,
            double dlpGycm_wk,
            double m_a, double m_b,
            double c_a, double c_b)
        {
            if (!(barrierHeight_m > 0)) barrierHeight_m = 1.0;
            if (!(ceilingHeight_m > 0)) return 0.0;
            if (!(dlpGycm_wk > 0)) return 0.0;

            double m = ExpFit(m_a, m_b, ceilingHeight_m);
            double C = ExpFit(c_a, c_b, ceilingHeight_m);

            if (!(m > 0) || !(C > 0)) return 0.0;

            // Martin/Sutton tertiary form:
            // K_ter(µGy/wk) = (C - m*h_b) * DLP(Gy·cm/wk)
            double k_uGy_wk = (C - m * barrierHeight_m) * dlpGycm_wk;

            if (double.IsNaN(k_uGy_wk) || double.IsInfinity(k_uGy_wk) || k_uGy_wk < 0)
                return 0.0;

            return k_uGy_wk;
        }
    }

    public static class CtDlpToK
    {
        public static double FromDlp(double dlp_mGycm, double k1_coeff, double k_per_cm)
        {
            if (!(dlp_mGycm > 0) || !(k1_coeff > 0) || !(k_per_cm > 0)) return 0.0;
            return dlp_mGycm * k1_coeff * k_per_cm;
        }
    }
}
