using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RadiationShieldingDesign
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Numeric-entry muscle memory: clicking or tabbing into a box selects its whole value,
            // so typing replaces it. A second click places the caret; drag and Shift+arrows select as usual.
            EventManager.RegisterClassHandler(typeof(TextBox), UIElement.GotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(SelectAllOnFocus));
            EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(FocusOnFirstClick));

            base.OnStartup(e);
        }

        private static void SelectAllOnFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb && !tb.IsReadOnly)
                tb.SelectAll();
        }

        private static void FocusOnFirstClick(object sender, MouseButtonEventArgs e)
        {
            // First click into an unfocused box: take focus and select all instead of dropping the caret.
            if (sender is TextBox tb && !tb.IsKeyboardFocusWithin && !tb.IsReadOnly)
            {
                e.Handled = true;
                tb.Focus();
            }
        }
    }
}
