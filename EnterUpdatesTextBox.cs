using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RadiationShieldingDesign
{
    public static class EnterUpdatesTextBox
    {
        public static readonly DependencyProperty UpdateOnEnterProperty =
            DependencyProperty.RegisterAttached(
                "UpdateOnEnter",
                typeof(bool),
                typeof(EnterUpdatesTextBox),
                new PropertyMetadata(false, OnUpdateOnEnterChanged));

        public static bool GetUpdateOnEnter(DependencyObject obj) =>
            (bool)obj.GetValue(UpdateOnEnterProperty);

        public static void SetUpdateOnEnter(DependencyObject obj, bool value) =>
            obj.SetValue(UpdateOnEnterProperty, value);

        private static void OnUpdateOnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox tb) return;

            if ((bool)e.NewValue)
                tb.KeyDown += Tb_KeyDown;
            else
                tb.KeyDown -= Tb_KeyDown;
        }

        private static void Tb_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            if (sender is TextBox tb)
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

            e.Handled = true; // prevent ding
        }
    }
}