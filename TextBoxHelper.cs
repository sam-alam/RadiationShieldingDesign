using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RadiationShieldingDesign
{
    public static class TextBoxHelper
    {
        public static readonly DependencyProperty IsNumericProperty =
            DependencyProperty.RegisterAttached(
                "IsNumeric",
                typeof(bool),
                typeof(TextBoxHelper),
                new PropertyMetadata(false, OnIsNumericChanged));

        public static void SetIsNumeric(DependencyObject element, bool value) => element.SetValue(IsNumericProperty, value);
        public static bool GetIsNumeric(DependencyObject element) => (bool)element.GetValue(IsNumericProperty);

        public static readonly DependencyProperty AllowDecimalProperty =
            DependencyProperty.RegisterAttached(
                "AllowDecimal",
                typeof(bool),
                typeof(TextBoxHelper),
                new PropertyMetadata(false));

        public static void SetAllowDecimal(DependencyObject element, bool value) => element.SetValue(AllowDecimalProperty, value);
        public static bool GetAllowDecimal(DependencyObject element) => (bool)element.GetValue(AllowDecimalProperty);

        private static void OnIsNumericChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox tb) return;

            if ((bool)e.NewValue)
            {
                tb.PreviewTextInput += OnPreviewTextInput;
                DataObject.AddPastingHandler(tb, OnPaste);
                tb.PreviewKeyDown += OnPreviewKeyDown;
            }
            else
            {
                tb.PreviewTextInput -= OnPreviewTextInput;
                DataObject.RemovePastingHandler(tb, OnPaste);
                tb.PreviewKeyDown -= OnPreviewKeyDown;
            }
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Let editing/navigation keys pass
            if (e.Key is Key.Back or Key.Delete or Key.Left or Key.Right or Key.Tab) return;
        }

        private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox tb) return;
            var allowDecimal = GetAllowDecimal(tb);
            var dec = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

            // Predict the text after this keystroke
            var selectionStart = tb.SelectionStart;
            var selectionLength = tb.SelectionLength;
            var newText = tb.Text.Remove(selectionStart, selectionLength)
                                 .Insert(selectionStart, e.Text);

            e.Handled = !IsValidNumeric(newText, allowDecimal, dec);
        }

        private static void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (!e.DataObject.GetDataPresent(DataFormats.Text)) { e.CancelCommand(); return; }

            var pasted = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            var allowDecimal = GetAllowDecimal(tb);
            var dec = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

            // Predict text after paste
            var selectionStart = tb.SelectionStart;
            var selectionLength = tb.SelectionLength;
            var newText = tb.Text.Remove(selectionStart, selectionLength)
                                 .Insert(selectionStart, pasted);

            if (!IsValidNumeric(newText, allowDecimal, dec))
                e.CancelCommand();
        }

        private static bool IsValidNumeric(string text, bool allowDecimal, string dec)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;               // allow empty while typing
            // Allow forms like ".", "1.", ".5" while editing if decimals allowed
            if (allowDecimal)
            {
                // Reject more than one decimal separator
                if (Regex.Matches(text, Regex.Escape(dec)).Count > 1) return false;
                // Valid: digits, optional one decimal separator
                var pattern = @"^\d*(\" + Regex.Escape(dec) + @")?\d*$";
                return Regex.IsMatch(text, pattern);
            }
            // Integers only
            return Regex.IsMatch(text, @"^\d*$");
        }
    }
}
