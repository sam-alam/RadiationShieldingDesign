using CommunityToolkit.Mvvm.ComponentModel;

namespace RadiationShieldingDesign
{
    public partial class PreexistingLayerViewModel : ObservableObject
    {
        [ObservableProperty] private string materialName = "Lead"; // Lead, Std Concrete, Lite Concrete, Steel, Gypsum, Wood, Lead-Eq
        [ObservableProperty] private double thicknessValue = 0.0;  // user input
        [ObservableProperty] private string thicknessUnit = "mm";  // mm/cm/in

        public double Thickness_mm
        {
            get
            {
                var unit = (ThicknessUnit ?? "mm").ToLowerInvariant();
                return unit switch
                {
                    "mm" => ThicknessValue,
                    "cm" => ThicknessValue * 10.0,
                    "in" => ThicknessValue * 25.4,
                    _ => ThicknessValue
                };
            }
        }

        // keep Thickness_mm live in bindings
        partial void OnThicknessUnitChanged(string value) => OnPropertyChanged(nameof(Thickness_mm));
        partial void OnThicknessValueChanged(double value) => OnPropertyChanged(nameof(Thickness_mm));
    }
}