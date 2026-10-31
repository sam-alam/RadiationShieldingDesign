namespace RadiationShieldingDesign.Engine;

public static class ShieldingCalculator
{
    public static BarrierResult Compute(BarrierRequest r, MaterialSpec m, double k_ter_wk)
    {
        double alpha = m.Alpha, beta = m.Beta, gamma = m.Gamma;

        double r2 = (r.Distance_m > 0) ? (r.Distance_m * r.Distance_m) : 1.0;
        double Nweek = Math.Max(0, r.N);

        // Weekly base component at barrier (this IS the part attenuated by the wall)
        double K_base_week_at_barrier = ((r.K1 * Nweek) / r2) * r.UseFactor * r.Occupancy;

        // Weekly tertiary component at barrier (bypass term; not attenuated by this wall)
        double K_ter_week_at_barrier = k_ter_wk * r.Occupancy;

        // Effective permissible weekly design goal left for the wall-handled component
        double P_eff = r.PermissibleP_mGy_week - K_ter_week_at_barrier;

        double x_mm;
        double term = double.NaN;

        if (P_eff <= 0.0)
        {
            // Tertiary alone already uses up / exceeds the design goal.
            // This wall cannot solve that by getting thicker.
            x_mm = double.PositiveInfinity;
        }
        else
        {
            term = K_base_week_at_barrier / Math.Max(1e-12, P_eff);

            double ratio = beta / alpha;
            double frac = (Math.Pow(term, gamma) + ratio) / (1.0 + ratio);
            x_mm = ((1.0 / (alpha * gamma)) * Math.Log(frac)) - r.PreShield_mm;
        }

        double K_perPt_at_barrier =
            ((r.K1 / r2) * r.UseFactor * r.Occupancy) +
            ((Nweek > 0) ? ((k_ter_wk * r.Occupancy) / Nweek) : 0.0);

        return new BarrierResult(
            r.BarrierId,
            Math.Max(0, x_mm),
            new()
            {
                ["K_pt @barrier (mGy/pt)"] = K_perPt_at_barrier,
                ["K_base_week @barrier (mGy/wk)"] = K_base_week_at_barrier,
                ["K_ter_week @barrier (mGy/wk)"] = K_ter_week_at_barrier,
                ["P_eff (mGy/wk) [P - k_ter]"] = P_eff,
             //   ["term"] = term,
                ["alpha"] = alpha,
                ["beta"] = beta,
                ["gamma"] = gamma
            }
        );
    }
}