using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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

            // Enter commits the value and moves to the next field (type → Enter → type → Enter).
            // handledEventsToo: existing Enter handlers (e.g. distance snap) still run first.
            EventManager.RegisterClassHandler(typeof(TextBox), UIElement.KeyDownEvent,
                new KeyEventHandler(EnterMovesToNextField), handledEventsToo: true);

            base.OnStartup(e);
        }

        private static void SelectAllOnFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb && !tb.IsReadOnly)
                tb.SelectAll();
        }

        private static void EnterMovesToNextField(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
            if (sender is not TextBox tb || tb.AcceptsReturn || IsInsideDataGrid(tb)) return;

            // Let any other Enter handling finish, then commit and move on.
            tb.Dispatcher.InvokeAsync(() =>
            {
                if (!tb.IsKeyboardFocusWithin) return;   // a handler already moved focus
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }, DispatcherPriority.Input);
        }

        private static bool IsInsideDataGrid(DependencyObject d)
        {
            for (var p = VisualTreeHelper.GetParent(d); p != null; p = VisualTreeHelper.GetParent(p))
                if (p is DataGrid) return true;
            return false;
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
