using System.Windows.Media;

namespace RadiationShieldingDesign
{
    public class LegendItem
    {
        // Key used to map color (e.g., "1/16\"")
        public string GaugeKey { get; }

        // Big label shown (e.g., 1/16")
        public string Label { get; }

        // Smaller subtext (e.g., "1.6 mm")
        public string Sub { get; }

        // Color chip brush
        public Brush Chip { get; }

        public LegendItem(string gaugeKey, string label, string sub, Brush chip)
        {
            GaugeKey = gaugeKey;
            Label = label;
            Sub = sub;
            Chip = chip;
        }
    }
}