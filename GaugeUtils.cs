namespace RadiationShieldingDesign
{
    public static class GaugeUtils
    {
        // Canonical Pb sheet "commercial" thicknesses (inches, label).
        // Store in inches, compute mm exactly (25.4 mm/in).
        private static readonly (double In, string Label)[] GaugeInchTable = new[]
        {
            (1.0/64.0,  "1/64\""),
            (1.0/32.0,  "1/32\""),
            (5.0/128.0, "5/128\""),
            (3.0/64.0,  "3/64\""),
            (1.0/16.0,  "1/16\""),
            (5.0/64.0,  "5/64\""),
            (3.0/32.0,  "3/32\""),
            (1.0/8.0,   "1/8\""),
            (5.0/32.0,  "5/32\""),
            (3.0/16.0,  "3/16\""),
            (1.0/4.0,   "1/4\""),
            (3.0/8.0,   "3/8\""),
            (1.0/2.0,   "1/2\""),
            (3.0/4.0,   "3/4\""),
            (1.0,     "1\""),
            (3.0/2.0, "1-1/2\""),
            (2.0,     "2\""),
        };

        // Public gauge table in mm, sorted small->large (used by legend ordering).
        public static readonly (double Mm, string Label)[] GaugeTable =
            GaugeInchTable
                .Select(t => (Mm: t.In * 25.4, Label: t.Label))
                .OrderBy(t => t.Mm)
                .ToArray();

        /// Return the next commercial gauge that is >= the required mm (CEILING).
        public static (string Label, double Mm) RoundUpGauge(double mm)
        {
            if (!(mm > 0)) return ("0\"", 0.0);

            var g = GaugeTable.FirstOrDefault(x => x.Mm >= mm);
            if (string.IsNullOrEmpty(g.Label))
            {
                // thicker than our max entry -> clamp to max available
                var last = GaugeTable[^1];
                return (last.Label, last.Mm);
            }
            return (g.Label, g.Mm);
        }

        /// (Optional) keep for any place you still want "nearest".
        public static (string Label, double Mm) NearestGauge(double mm)
        {
            var g = GaugeTable.OrderBy(x => Math.Abs(x.Mm - mm)).First();
            return (g.Label, g.Mm);
        }

        /// For consistent legend ordering (small -> large).
        public static int GaugeSortKey(string label)
        {
            var found = GaugeTable
                .Select((t, i) => new { t.Label, Index = i })
                .FirstOrDefault(x => string.Equals(x.Label, label, StringComparison.Ordinal));
            return found?.Index ?? int.MaxValue;
        }
    }
}
