//using System.IO.Packaging;
using DocumentFormat.OpenXml;
//using DocumentFormat.OpenXml.InkML;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
//using Drawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;
//using System.Drawing.Common;
// MainWindow.xaml.cs
using Microsoft.Win32;
using System.ComponentModel;
using System.Linq;
//using System.Collections.ObjectModel;
using System.Globalization;
//using System.Windows.Shapes;
//using Microsoft.VisualBasic;
//using System.Collections.Generic;
//using System.Linq;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
// OpenXML drawing aliases to avoid ambiguity with WPF's Media.Drawing
using A = DocumentFormat.OpenXml.Drawing;
using Drawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;


namespace RadiationShieldingDesign
{
    public partial class MainWindow : Window
    {
        private MainViewModel VM => (MainViewModel)DataContext;
        private BarrierItemViewModel? _dragBarrier = null;
        private bool _scaleFirstPointSet = false;
        private const double HitTol = 12.0; // pixels
        private bool _dragTube = false;
        private Point _lastPos;
        private bool _dragEnd1, _dragEnd2, _dragWhole;
        private bool _dragLabel;
        private BarrierItemViewModel? _labelBarrier;

        private bool _dragBg = false;
        private Point _bgLast;
        public MainWindow()
        {
            InitializeComponent();
            this.Focusable = true;
            this.Focus();
            Keyboard.Focus(this);

            DataContext = new MainViewModel();
            VM.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.CurrentProjectPath) ||
                    e.PropertyName == nameof(MainViewModel.IsDirty))
                {
                    UpdateWindowTitle();
                }
            };

            UpdateWindowTitle();

            VM.BeginProjectLoad();
            try
            {
                if (!VM.Barriers.Any())
                    VM.GetType().GetMethod("LoadDefaultLayoutForRoom", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?.Invoke(VM, null);
            }
            finally
            {
                VM.EndProjectLoad();   // should result in not-dirty
            }
        }

        private void UpdateWindowTitle()
        {
            var name = string.IsNullOrWhiteSpace(VM.CurrentProjectPath)
                ? "Untitled"
                : System.IO.Path.GetFileName(VM.CurrentProjectPath);

            var dirty = VM.IsDirty ? " *" : "";
            Title = $"Radiation Shielding Design — {name}{dirty}";
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            VM.RecomputeDistancesForAll();
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(DesignCanvas);

            // If editing background, start panning it
            if (VM.IsEditingBackground && VM.BackgroundImage != null)
            {
                _dragBg = true;
                _bgLast = pos;
                DesignCanvas.Cursor = Cursors.SizeAll;
                Mouse.Capture(DesignCanvas);
                return;
            }
            // --- Measure scale mode ---
            if (VM.IsMeasuringScale)
            {
                if (!_scaleFirstPointSet)
                {
                    VM.MeasureX1 = pos.X; VM.MeasureY1 = pos.Y;
                    VM.MeasureX2 = pos.X; VM.MeasureY2 = pos.Y;
                    _scaleFirstPointSet = true;
                }
                else
                {
                    VM.MeasureX2 = pos.X; VM.MeasureY2 = pos.Y;
                    FinishScaleMeasure();
                }
                return;
            }

            //measure distance mode
            if (VM.IsMeasuringDistance)
            {
                if (!_scaleFirstPointSet) // Reuse flag for simplicity
                {
                    VM.MeasureX1 = pos.X; VM.MeasureY1 = pos.Y;
                    VM.MeasureX2 = pos.X; VM.MeasureY2 = pos.Y;
                    _scaleFirstPointSet = true;
                }
                else
                {
                    VM.MeasureX2 = pos.X; VM.MeasureY2 = pos.Y;
                    double dx = VM.MeasureX2 - VM.MeasureX1;
                    double dy = VM.MeasureY2 - VM.MeasureY1;
                    double pixels = Math.Sqrt(dx * dx + dy * dy);
                    double realM = pixels * VM.CmPerPixel / 100.0;
                    double realFt = realM / 0.3048;
                    MessageBox.Show($"Distance: {realM:F3} m ({realFt:F2} ft)");
                    VM.IsMeasuringDistance = false;
                    _scaleFirstPointSet = false;
                }
                return;
            }

            //label hit?
            var hitResult = VisualTreeHelper.HitTest(DesignCanvas, pos);
            if (hitResult?.VisualHit is TextBlock tb && tb.DataContext is BarrierItemViewModel labelBarrier)
            {
                VM.SelectedBarrier = labelBarrier; // Optional: select the barrier
                _dragLabel = true;
                _labelBarrier = labelBarrier;
                _lastPos = pos;
                if (_dragLabel) DesignCanvas.Cursor = Cursors.SizeAll;
                Mouse.Capture(DesignCanvas);
                e.Handled = true;
                return;
            }

            // --- Tube hit? ---
            if (Math.Abs(pos.X - VM.TubeX) < HitTol && Math.Abs(pos.Y - VM.TubeY) < HitTol)
            {
                _dragLabel = false; _labelBarrier = null;
                _dragTube = true;
                _lastPos = pos;
                if (_dragTube) DesignCanvas.Cursor = Cursors.SizeAll;
                Mouse.Capture(DesignCanvas);
                return;
            }

            // --- Prefer endpoint hit for currently selected barrier (feels snappier) ---
            var sel = VM.SelectedBarrier;
            if (sel is not null && sel.IsDrawable)
            {
                if (Distance(pos.X, pos.Y, sel.X1, sel.Y1) < HitTol)
                {
                    _dragLabel = false; _labelBarrier = null;
                    _dragBarrier = sel; _dragEnd1 = true; _lastPos = pos;
                    if (_dragEnd1 || _dragEnd2)
                        DesignCanvas.Cursor = Cursors.Cross;
                    Mouse.Capture(DesignCanvas);
                    return;
                }
                if (Distance(pos.X, pos.Y, sel.X2, sel.Y2) < HitTol)
                {
                    _dragLabel = false; _labelBarrier = null;
                    _dragBarrier = sel; _dragEnd2 = true; _lastPos = pos;
                    if (_dragEnd1 || _dragEnd2)
                        DesignCanvas.Cursor = Cursors.Cross;
                    Mouse.Capture(DesignCanvas);
                    return;
                }
            }

            // --- Hit-test all barriers: endpoints first, then near the line (whole move) ---
            BarrierItemViewModel? hit = null;
            bool hitP1 = false, hitP2 = false;

            foreach (var b in VM.Barriers)
            {
                if (!b.IsDrawable) continue;
                if (Distance(pos.X, pos.Y, b.X1, b.Y1) < HitTol) { hit = b; hitP1 = true; break; }
                if (Distance(pos.X, pos.Y, b.X2, b.Y2) < HitTol) { hit = b; hitP2 = true; break; }
            }

            if (hit is null)
            {
                double best = HitTol;
                foreach (var b in VM.Barriers)
                {
                    if (!b.IsDrawable) continue;
                    double d = DistancePointToSegment(pos.X, pos.Y, b.X1, b.Y1, b.X2, b.Y2);
                    if (d < best) { best = d; hit = b; }
                }
            }

            if (hit is not null)
            {
                VM.SelectedBarrier = hit;
                _dragBarrier = hit;
                _dragEnd1 = hitP1;
                _dragEnd2 = hitP2;
                _dragWhole = !(hitP1 || hitP2);
                _lastPos = pos;
                Mouse.Capture(DesignCanvas);
                if (_dragWhole || _dragLabel)
                    DesignCanvas.Cursor = Cursors.SizeAll; // Move cursor for whole line or label
                else if (_dragEnd1 || _dragEnd2)
                    DesignCanvas.Cursor = Cursors.Cross; // Cross for endpoint resize
            }
            else
            {
                // click on empty space clears selection and any drag flags
                VM.SelectedBarrier = null;
                _dragBarrier = null;
                _dragEnd1 = _dragEnd2 = _dragWhole = false;
                _dragLabel = false; _labelBarrier = null;
                _dragTube = false;
                Mouse.Capture(null);
                DesignCanvas.Cursor = Cursors.Arrow;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(DesignCanvas);
            // pan background while editing
            if (_dragBg)
            {
                var dxe = pos.X - _bgLast.X;
                var dye = pos.Y - _bgLast.Y;
                VM.BackgroundOffsetX += dxe;
                VM.BackgroundOffsetY += dye;
                _bgLast = pos;
                return;
            }

            // live preview of the measuring line
            if (VM.IsMeasuringScale && _scaleFirstPointSet)
            {
                VM.MeasureX2 = pos.X; VM.MeasureY2 = pos.Y;
                return;
            }

            //measure distance live
            if (VM.IsMeasuringDistance && _scaleFirstPointSet) { VM.MeasureX2 = pos.X; VM.MeasureY2 = pos.Y; }

            // label drag
            if (_dragLabel && _labelBarrier != null)
            {
                double dxx = pos.X - _lastPos.X;
                double dyy = pos.Y - _lastPos.Y;

                _labelBarrier.LabelX += dxx;
                _labelBarrier.LabelY += dyy;

                // NEW: keep offsets in sync so save/load works
                _labelBarrier.SyncLabelOffsetAlongFromXY();

                _lastPos = pos;
                return;
            }

            // tube drag
            if (_dragTube)
            {
                VM.TubeX = pos.X;
                VM.TubeY = pos.Y;

                foreach (var b in VM.Barriers)
                {
                    UpdateDistanceForBarrier(b);
                    
                }
                return;
            }

            // barrier drag
            if (_dragBarrier is null) return;

            double dx = pos.X - _lastPos.X;
            double dy = pos.Y - _lastPos.Y;

            if (_dragEnd1)
            {
                _dragBarrier.X1 += dx; _dragBarrier.Y1 += dy;
            }
            else if (_dragEnd2)
            {
                _dragBarrier.X2 += dx; _dragBarrier.Y2 += dy;
            }
            else if (_dragWhole)
            {
                _dragBarrier.X1 += dx; _dragBarrier.Y1 += dy;
                _dragBarrier.X2 += dx; _dragBarrier.Y2 += dy;
            }

            // update distances for this barrier only (fast)
            if (_dragBarrier.DistanceOverride_m > 0)
            {
                _lastPos = pos;
                return;
            }

            if (_dragBarrier.SaveDistance && _dragBarrier.SavedRawDistance_m > 0)
            {
                // Saved distance in effect → keep showing the saved value (do not change)
                _dragBarrier.DisplayDistance_m = _dragBarrier.SavedRawDistance_m;
                _dragBarrier.AutoDistance_m = _dragBarrier.SavedRawDistance_m;// + _dragBarrier.ExtraOffset_m;
                _lastPos = pos;
                return;
            }

            // normal live update from canvas
            var rawB = VM.ComputeCanvasDistance_m(_dragBarrier);
            _dragBarrier.DisplayDistance_m = rawB;
            _dragBarrier.AutoDistance_m = rawB;// + _dragBarrier.ExtraOffset_m;

            _lastPos = pos;
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DesignCanvas.Cursor = Cursors.Arrow;
            _dragBg = false;
            _dragTube = _dragEnd1 = _dragEnd2 = _dragWhole = false;
            _dragBarrier = null;
            var lb = _labelBarrier;
            _dragLabel = false;
            _labelBarrier = null;
            if (lb != null) lb.SyncLabelOffsetAlongFromXY();

            Mouse.Capture(null);
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (VM.IsMeasuringScale)
            {
                VM.IsMeasuringScale = false;
                _scaleFirstPointSet = false;
                e.Handled = true;
            }
            if (VM.IsMeasuringDistance)
            {
                VM.IsMeasuringDistance = false;
                _scaleFirstPointSet = false;
                e.Handled = true;
            }
        }

        private static double Distance(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx;
            double dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double DistancePointToSegment(double px, double py, double x1, double y1, double x2, double y2)
        {
            double vx = x2 - x1, vy = y2 - y1;
            double wx = px - x1, wy = py - y1;
            double c1 = vx * wx + vy * wy;
            if (c1 <= 0) return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));
            double c2 = vx * vx + vy * vy;
            if (c2 <= c1) return Math.Sqrt((px - x2) * (px - x2) + (py - y2) * (py - y2));
            double t = c1 / c2;
            double projx = x1 + t * vx, projy = y1 + t * vy;
            return Math.Sqrt((px - projx) * (px - projx) + (py - projy) * (py - projy));
        }


        private void FinishScaleMeasure()
        {
            double dx = VM.MeasureX2 - VM.MeasureX1;
            double dy = VM.MeasureY2 - VM.MeasureY1;
            double pixels = Math.Sqrt(dx * dx + dy * dy);

            string s = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter real distance (e.g., 7'4\"  |  2.235 m  |  223.5 cm):",
                "Set scale", "7'4\"");

            double realCm = TryParseLengthToCm(s);
            if (realCm > 0 && pixels > 1)
            {
                VM.CmPerPixel = Math.Round(realCm / pixels, 2);
                VM.RecomputeDistancesForAll();
            }

            VM.IsMeasuringScale = false;
            _scaleFirstPointSet = false;
        }

        private static double TryParseLengthToCm(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Trim().ToLowerInvariant();

            if (s.Contains("'") || s.Contains("ft"))
            { // feet & inches
                double ft = 0, inch = 0;
                int ftIdx = s.IndexOf('\'');
                if (ftIdx >= 0)
                {
                    double.TryParse(s[..ftIdx], out ft);
                    var rest = s[(ftIdx + 1)..];
                    int q = rest.IndexOf('"');
                    if (q >= 0) double.TryParse(rest[..q], out inch);
                    else double.TryParse(rest, out inch);
                }
                else
                {
                    var parts = s.Replace("inches", "in").Replace("inch", "in").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i] == "ft" && i > 0) double.TryParse(parts[i - 1], out ft);
                        if ((parts[i] == "in" || parts[i].EndsWith("in")) && i > 0) double.TryParse(parts[i - 1], out inch);
                    }
                    if (s.Contains("ft") && !s.Contains("in"))
                    {
                        var num = parts.FirstOrDefault(p => double.TryParse(p, out _));
                        if (double.TryParse(num, out var feetOnly)) { ft = feetOnly; inch = 0; }
                    }
                }
                return (ft * 12.0 + inch) * 2.54;
            }
            if (s.EndsWith("\"") || s.Contains(" in"))
            {
                string num = s.Replace("\"", "").Replace("in", "");
                if (double.TryParse(num, out var inches)) return inches * 2.54;
            }
            if (s.Contains(" m") && double.TryParse(s.Replace(" m", ""), out var m)) return m * 100.0;
            if (s.Contains(" cm") && double.TryParse(s.Replace(" cm", ""), out var cm)) return cm;
            if (double.TryParse(s, out var bare)) return bare < 10 ? bare * 100.0 : bare;
            return 0;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // don’t interfere when typing
            if (!IsTypingInTextBox())
            {
                // fine move in pixels; Shift = bigger move; Ctrl = smaller move
                double step = 2.0;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) step = 10.0;
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) step = 1.0;

                if (e.Key == Key.Left) { MoveSelectedBarrier(-step, 0); e.Handled = true; return; }
                if (e.Key == Key.Right) { MoveSelectedBarrier(step, 0); e.Handled = true; return; }
                if (e.Key == Key.Up) { MoveSelectedBarrier(0, -step); e.Handled = true; return; }
                if (e.Key == Key.Down) { MoveSelectedBarrier(0, step); e.Handled = true; return; }

                // “A” to straighten to closest axis
                if (e.Key == Key.A) { StraightenSelectedBarrierToAxis(); e.Handled = true; return; }

                // Optional: Ctrl+A (if you still want it) — but note this conflicts with Select-All in textboxes
                // if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.A)
                // { StraightenSelectedBarrierToAxis(); e.Handled = true; return; }
            }

            // keep your existing Escape handling
            if (e.Key == Key.Escape && VM.IsMeasuringScale)
            {
                VM.IsMeasuringScale = false;
                _scaleFirstPointSet = false;
                e.Handled = true;
            }
            if (e.Key == Key.Escape && VM.IsMeasuringDistance)
            {
                VM.IsMeasuringDistance = false;
                _scaleFirstPointSet = false;
                e.Handled = true;
            }

            base.OnPreviewKeyDown(e);
        }


        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if (VM.IsEditingBackground && VM.BackgroundImage != null)
            {
                VM.BackgroundScale *= (e.Delta > 0) ? 1.10 : (1.0 / 1.10);
                e.Handled = true;
                return;
            }
            base.OnPreviewMouseWheel(e);
        }

        private void OverrideDistanceTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Ensure any pending binding pushes its text (even if identical)
                if (sender is TextBox tb)
                {
                    var be = BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
                    be?.UpdateSource(); // commits the text, tolerates "same value"
                }

                // Force the snap even if the numeric value didn't change
                VM.ResnapSelectedBarrierToOverride();
                e.Handled = true;
            }
        }

        private double _savedRightPanelV;
        private double _savedRightPanelH;


        private T WithSelectionHidden<T>(Func<T> work)
        {
            var vm = DataContext as MainViewModel;
            _savedRightPanelV = RightPanelScroll?.VerticalOffset ?? 0;
            _savedRightPanelH = RightPanelScroll?.HorizontalOffset ?? 0;

            // snapshot barrier selections and the listbox selection
            var selectedBarriers = vm?.Barriers?.Where(b => b?.IsSelected == true).ToList() ?? new List<BarrierItemViewModel>();
            var prevListSelection = BarrierList?.SelectedItem as BarrierItemViewModel;

            try
            {
                // release any mouse capture to avoid hover/focus visuals
                System.Windows.Input.Mouse.Capture(null);

                // clear barrier selection flags (canvas highlight)
                foreach (var b in selectedBarriers) b.IsSelected = false;

                // clear list selection (right-side highlight) without touching styles
                if (BarrierList != null) BarrierList.SelectedItem = null;

                // move focus away from list/canvas so focus styles don’t show
                this.Focus();
                this.UpdateLayout();

                // perform the captures
                return work();
            }
            finally
            {
                // restore selections
                if (BarrierList != null) BarrierList.SelectedItem = prevListSelection;
                foreach (var b in selectedBarriers) b.IsSelected = true;

                this.UpdateLayout();

                if (RightPanelScroll != null)
                {
                    var v = _savedRightPanelV;
                    var h = _savedRightPanelH;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        RightPanelScroll.ScrollToHorizontalOffset(h);
                        RightPanelScroll.ScrollToVerticalOffset(v);
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }


        private Rect GetCanvasContentBounds()
        {
            var vm = DataContext as MainViewModel;

            double hostW = (DesignCanvas?.ActualWidth > 1) ? DesignCanvas.ActualWidth : 800;
            double hostH = (DesignCanvas?.ActualHeight > 1) ? DesignCanvas.ActualHeight : 600;

            if (vm?.Barriers is null || vm.Barriers.Count == 0)
                return new Rect(0, 0, hostW, hostH);

            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;

            foreach (var b in vm.Barriers)
            {
                // if your properties are nullable, coerce with ?? 0
                double x1 = b.X1;
                double x2 = b.X2;
                double y1 = b.Y1;
                double y2 = b.Y2;


                if (double.IsNaN(x1) || double.IsNaN(x2) || double.IsNaN(y1) || double.IsNaN(y2))
                    continue;

                // per-barrier mins/maxes (no Math.Min/Max to avoid overload shenanigans)
                double bxmin = (x1 <= x2) ? x1 : x2;
                double bxmax = (x1 >= x2) ? x1 : x2;
                double bymin = (y1 <= y2) ? y1 : y2;
                double bymax = (y1 >= y2) ? y1 : y2;

                if (bxmin < minX) minX = bxmin;
                if (bymin < minY) minY = bymin;
                if (bxmax > maxX) maxX = bxmax;
                if (bymax > maxY) maxY = bymax;
            }

            if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(maxX) || !double.IsFinite(maxY))
                return new Rect(0, 0, hostW, hostH);

            const double pad = 40; // pixels of breathing room
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(hostW, maxX + pad);
            maxY = Math.Min(hostH, maxY + pad);

            double w = Math.Max(1, maxX - minX);
            double h = Math.Max(1, maxY - minY);
            return new Rect(minX, minY, w, h);
        }



        private static (byte[] png, int pxW, int pxH) CaptureElementPng(
    FrameworkElement element, double dpi = 288, bool captureFullScrollable = true)
        {
            element.UpdateLayout();

            (double w, double h) = GetCaptureSizeDip(element, captureFullScrollable);

            if (w <= 0 || h <= 0) return (Array.Empty<byte>(), 0, 0);

            int pxW = (int)Math.Ceiling(w * dpi / 96.0);
            int pxH = (int)Math.Ceiling(h * dpi / 96.0);

            // WPF RenderTargetBitmap has practical limits; avoid huge crashes
            const int MaxPx = 32000;
            pxW = Math.Min(pxW, MaxPx);
            pxH = Math.Min(pxH, MaxPx);

            var rtb = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);

            var brush = new VisualBrush(element)
            {
                Viewbox = new Rect(0, 0, w, h),
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };
            if (brush.CanFreeze) brush.Freeze();

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
            }

            rtb.Render(dv);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return (ms.ToArray(), rtb.PixelWidth, rtb.PixelHeight);
        }

        private static (double w, double h) GetCaptureSizeDip(FrameworkElement element, bool full)
        {
            double w = element.ActualWidth;
            double h = element.ActualHeight;

            if (!full) return (w, h);

            // If the element IS a ScrollViewer, use it directly
            if (element is ScrollViewer sv0)
                return (Math.Max(w, sv0.ExtentWidth), Math.Max(h, sv0.ExtentHeight));

            // Otherwise try to find an ancestor ScrollViewer that hosts this element
            var sv = FindAncestorScrollViewer(element);
            if (sv != null)
                return (Math.Max(w, sv.ExtentWidth), Math.Max(h, sv.ExtentHeight));

            // Fallback: measure to desired size (works for many panels)
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var ds = element.DesiredSize;
            return (Math.Max(w, ds.Width), Math.Max(h, ds.Height));
        }

        private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? d)
        {
            DependencyObject? cur = d;
            while (cur != null)
            {
                if (cur is ScrollViewer sv) return sv;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return null;
        }


        private static (byte[] png, int pxW, int pxH) CaptureElementRegionPng(
    FrameworkElement element, Rect regionDip, double dpi = 288)
        {
            element.UpdateLayout();

            int pxW = (int)Math.Ceiling(regionDip.Width * dpi / 96.0);
            int pxH = (int)Math.Ceiling(regionDip.Height * dpi / 96.0);
            if (pxW <= 0 || pxH <= 0) return (Array.Empty<byte>(), 0, 0);

            var rtb = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);

            var brush = new VisualBrush(element)
            {
                Viewbox = regionDip,
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };

            if (brush.CanFreeze) brush.Freeze();

            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(dv, EdgeMode.Aliased);

            using (var dc = dv.RenderOpen())
            {
                var targetRect = new Rect(0, 0, regionDip.Width, regionDip.Height);
                dc.DrawRectangle(Brushes.Transparent, null, targetRect);
                dc.DrawRectangle(brush, null, targetRect);
            }

            rtb.Render(dv);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return (ms.ToArray(), rtb.PixelWidth, rtb.PixelHeight);
        }



        private static long InchesToEmu(double inches) => (long)(inches * 914400.0);
        private static long PxToEmu(int px, double dpi) => (long)(px * (914400.0 / dpi));
        private static uint _imgCounter = 1;

        // NOTE: we now accept pxW/pxH directly, no BitmapDecoder needed
        private static W.Paragraph BuildImageParagraph(
    WordprocessingDocument doc,
    byte[] pngBytes,
    int pxW,
    int pxH,
    double contentWidthInches,
    double srcDpi,
    double? maxHeightInches = null)
        {
            var main = doc.MainDocumentPart ?? doc.AddMainDocumentPart();
            main.Document ??= new W.Document(new W.Body());

            var imgPart = main.AddImagePart(ImagePartType.Png);
            using (var s = new MemoryStream(pngBytes)) imgPart.FeedData(s);
            var relId = main.GetIdOfPart(imgPart);

            long maxCx = InchesToEmu(contentWidthInches);

            double scale = maxCx / (double)PxToEmu(pxW, srcDpi);

            if (maxHeightInches.HasValue)
            {
                long maxCy = InchesToEmu(maxHeightInches.Value);
                long cyAtWidth = (long)(PxToEmu(pxH, srcDpi) * scale);
                if (cyAtWidth > maxCy)
                {
                    double scaleH = maxCy / (double)PxToEmu(pxH, srcDpi);
                    scale = Math.Min(scale, scaleH);
                }
            }

            long cx = (long)(PxToEmu(pxW, srcDpi) * scale);
            long cy = (long)(PxToEmu(pxH, srcDpi) * scale);

            uint id = _imgCounter++;
            var drawing =
                new Drawing(
                    new DW.Inline(
                        new DW.Extent() { Cx = cx, Cy = cy },
                        new DW.EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                        new DW.DocProperties() { Id = id, Name = $"Image{id}" },
                        new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks() { NoChangeAspect = true }),
                        new A.Graphic(
                            new A.GraphicData(
                                new PIC.Picture(
                                    new PIC.NonVisualPictureProperties(
                                        new PIC.NonVisualDrawingProperties() { Id = 0U, Name = $"img{id}.png" },
                                        new PIC.NonVisualPictureDrawingProperties()),
                                    new PIC.BlipFill(
                                        new A.Blip() { Embed = relId },
                                        new A.Stretch(new A.FillRectangle())),
                                    new PIC.ShapeProperties(
                                        new A.Transform2D(
                                            new A.Offset() { X = 0L, Y = 0L },
                                            new A.Extents() { Cx = cx, Cy = cy }),
                                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })
                                )
                            )
                            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                        )
                    )
                );

            return new W.Paragraph(new W.Run(drawing))
            {
                ParagraphProperties = new W.ParagraphProperties(
                    new W.Justification { Val = W.JustificationValues.Center },
                    new W.SpacingBetweenLines { After = "120" })
            };
        }

        private static void ConfigurePageToLetterPortrait(WordprocessingDocument doc)
        {
            var main = doc.MainDocumentPart ?? doc.AddMainDocumentPart();
            main.Document ??= new Document(new Body());
            var body = main.Document.Body!;
            var sect = body.GetFirstChild<SectionProperties>();
            if (sect is null)
            {
                sect = new SectionProperties();
                body.Append(sect);
            }

            var size = new PageSize { Width = (UInt32Value)(8.5 * 1440), Height = (UInt32Value)(11.0 * 1440), Orient = PageOrientationValues.Portrait };
            var mar = new PageMargin { Top = 720, Right = 720, Bottom = 720, Left = 720 };

            sect.RemoveAllChildren<PageSize>();
            sect.RemoveAllChildren<PageMargin>();
            sect.Append(size);
            sect.Append(mar);
        }
        private enum TableAutoFitMode
        {
            None,           // keep current fixed widths
            ToWindow,       // Word: AutoFit -> Window
            ToContent       // Word: AutoFit -> Contents
        }

        private static void ApplyTableAutoFit(W.Table table, TableAutoFitMode mode)
        {
            if (mode == TableAutoFitMode.None) return;

            var tblPr = table.GetFirstChild<W.TableProperties>();
            if (tblPr == null)
            {
                tblPr = new W.TableProperties();
                table.PrependChild(tblPr);
            }

            // Switch to autofit layout
            tblPr.RemoveAllChildren<W.TableLayout>();
            tblPr.Append(new W.TableLayout { Type = W.TableLayoutValues.Autofit });

            // Remove fixed grid and fixed cell widths (otherwise Word can’t autofit)
            table.RemoveAllChildren<W.TableGrid>();

            foreach (var cell in table.Descendants<W.TableCell>())
            {
                var tcp = cell.GetFirstChild<W.TableCellProperties>();
                if (tcp == null) continue;
                tcp.RemoveAllChildren<W.TableCellWidth>();
            }

            // Table width behavior
            tblPr.RemoveAllChildren<W.TableWidth>();
            if (mode == TableAutoFitMode.ToWindow)
            {
                // 5000 = 100% in Pct units
                tblPr.Append(new W.TableWidth { Type = W.TableWidthUnitValues.Pct, Width = "10000" });
            }
            else // ToContent
            {
                // Auto width
                tblPr.Append(new W.TableWidth { Type = W.TableWidthUnitValues.Auto, Width = "0" });
            }
        }

        private static W.Table MakeResultsTable(IEnumerable<BarrierItemViewModel> barriers)
        {
            // Column widths in TWIPs (1 inch = 1440 twips). These are conservative and fit Letter w/ 1" margins.
            // Total available width ~ 6.5" => 9360 twips. We keep total ~9300.
            const int wLabel = 900;
            const int wType = 1200;
            const int wDesig = 1400;
            const int wArea = 1800;
            const int wOcc = 900;
            const int wDist = 900;
            const int wThk = 1200;

            // Helpers
            static W.TableCellWidth TCW(int twips) => new W.TableCellWidth { Type = W.TableWidthUnitValues.Dxa, Width = twips.ToString(CultureInfo.InvariantCulture) };

            static W.Paragraph MakePara(string text, bool bold = false, bool right = false)
            {
                var rPr = new W.RunProperties(new W.FontSize { Val = "20" }); // 10pt
                if (bold) rPr.Append(new W.Bold());

                return new W.Paragraph(new W.Run(rPr, new W.Text(text ?? "")))
                {
                    ParagraphProperties = new W.ParagraphProperties(
                        new W.Justification { Val = right ? W.JustificationValues.Right : W.JustificationValues.Left },
                        new W.SpacingBetweenLines { After = "0" })
                };
            }

            static W.TableCell MakeCell(string text, int widthTwips, bool bold = false, bool right = false, string? shadingFill = null)
            {
                var props = new W.TableCellProperties(TCW(widthTwips));
                if (!string.IsNullOrWhiteSpace(shadingFill))
                    props.Append(new W.Shading { Fill = shadingFill, Val = W.ShadingPatternValues.Clear });

                return new W.TableCell(props, MakePara(text, bold: bold, right: right));
            }

            var tbl = new W.Table(
                new W.TableProperties(
                    new W.TableWidth { Type = W.TableWidthUnitValues.Dxa, Width = "9300" },
                    new W.TableLayout { Type = W.TableLayoutValues.Fixed },
                    new W.TableBorders(
                        new W.TopBorder { Val = W.BorderValues.Single, Size = 8 },
                        new W.BottomBorder { Val = W.BorderValues.Single, Size = 8 },
                        new W.LeftBorder { Val = W.BorderValues.Single, Size = 8 },
                        new W.RightBorder { Val = W.BorderValues.Single, Size = 8 },
                        new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 6 },
                        new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 6 }
                    )
                ),
                new W.TableGrid(
                    new W.GridColumn() { Width = wLabel.ToString(CultureInfo.InvariantCulture) },
                    new W.GridColumn() { Width = wType.ToString(CultureInfo.InvariantCulture) },
                    new W.GridColumn() { Width = wDesig.ToString(CultureInfo.InvariantCulture) },
                    new W.GridColumn() { Width = wArea.ToString(CultureInfo.InvariantCulture) },
                    new W.GridColumn() { Width = wOcc.ToString(CultureInfo.InvariantCulture) },
                    new W.GridColumn() { Width = wDist.ToString(CultureInfo.InvariantCulture) },
                    new W.GridColumn() { Width = wThk.ToString(CultureInfo.InvariantCulture) }
                )
            );

            // Header row (bold + darker gray)
            const string headerFill = "C0C0C0"; // darker gray
            var header = new W.TableRow(
                MakeCell("Label", wLabel, bold: true, right: false, shadingFill: headerFill),
                MakeCell("Barrier Type", wType, bold: true, right: false, shadingFill: headerFill),
                MakeCell("Barrier Designation", wDesig, bold: true, right: false, shadingFill: headerFill),
                MakeCell("Protected Area", wArea, bold: true, right: false, shadingFill: headerFill),
                MakeCell("Occupancy", wOcc, bold: true, right: true, shadingFill: headerFill),
                MakeCell("Distance", wDist, bold: true, right: true, shadingFill: headerFill),
                MakeCell("Total Required Shielding", wThk, bold: true, right: true, shadingFill: headerFill)
            );
            tbl.Append(header);

            // Data rows
            foreach (var b in barriers)
            {
                string label = b.Label ?? "";
                string kind = b.Kind.ToString();
                string desig = b.Designation.ToString().Replace('_', ' ');
                string area = b.ProtectedArea ?? "";

                // numeric columns -> right aligned
                string occ = b.Occupancy.ToString("0.###", CultureInfo.InvariantCulture);

                // Distance in ft
                double ft = b.EffectiveDistance_m / 0.3048;
                string dis = ft.ToString("0.0", CultureInfo.InvariantCulture) + " ft";

                // Thickness display (report-ready)
                string reqDisp = !string.IsNullOrWhiteSpace(b.ThicknessShortDisplay)
                    ? b.ThicknessShortDisplay
                    : (b.RequiredThickness_mm > 0 ? (b.RequiredThickness_mm / 25.4).ToString("0.###", CultureInfo.InvariantCulture) + "\""
                                                  : "—");

                string thick;

                if (b.HasThicknessRounding && !string.IsNullOrWhiteSpace(b.FinalThicknessShortDisplay))
                {
                    thick = $"Required: {reqDisp}; Specified: {b.FinalThicknessShortDisplay}";
                }
                else
                {
                    // No rounding: show the required value only
                    thick = reqDisp;
                }

                var row = new W.TableRow(
                    MakeCell(label, wLabel, right: false),
                    MakeCell(kind, wType, right: false),
                    MakeCell(desig, wDesig, right: false),
                    MakeCell(area, wArea, right: false),
                    MakeCell(occ, wOcc, right: true),
                    MakeCell(dis, wDist, right: true),
                    MakeCell(thick, wThk, right: true)
                );

                tbl.Append(row);
            }

            return tbl;
        }


       
        private static Rect InflateAndClampRegion(Rect r, FrameworkElement host, double padDip)
        {
            if (r.IsEmpty) return r;

            // Inflate
            r = new Rect(r.X - padDip, r.Y - padDip, r.Width + 2 * padDip, r.Height + 2 * padDip);

            // Clamp to host bounds (DIPs)
            var maxW = Math.Max(0, host.ActualWidth);
            var maxH = Math.Max(0, host.ActualHeight);

            // If your canvas is larger than viewport, you may want ActualWidth/Height of the Canvas itself.
            // For a Canvas, ActualWidth/Height are usually the canvas size, so this is OK.

            double x = Math.Max(0, r.X);
            double y = Math.Max(0, r.Y);
            double w = Math.Min(r.Width - (x - r.X), maxW - x);
            double h = Math.Min(r.Height - (y - r.Y), maxH - y);

            return (w <= 0 || h <= 0) ? Rect.Empty : new Rect(x, y, w, h);
        }


       private sealed record LegendItem(string GaugeKey, string Label, Brush Brush);

        private IEnumerable<LegendItem> BuildLegendItems(IEnumerable<BarrierItemViewModel> barriers)
        {
            // Uses the SAME converter as your canvas strokes
            var converter = TryFindResource("GaugeToBrushConverter") as IValueConverter;

            // Build unique items by Label (so you get "1/32\" Lead", "5/8\" Gypsum", etc.)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var b in barriers ?? Enumerable.Empty<BarrierItemViewModel>())
            {
                if (b is null) continue;
                if (b.Designation == BarrierDesignation.Floor ||
        b.Designation == BarrierDesignation.Ceiling)
                    continue;
                if (b.RequiredThickness_mm <= 0) continue;
                if (string.IsNullOrWhiteSpace(b.MaterialName)) continue;

                string label = MakeLegendLabel(b);
                if (!seen.Add(label)) continue;

                Brush brush = Brushes.Gray;
                if (converter != null)
                {
                    var converted = converter.Convert(b.GaugeKey, typeof(Brush), null, CultureInfo.CurrentCulture);
                    if (converted is Brush br) brush = br;
                }

                yield return new LegendItem(b.GaugeKey, label, brush);
            }
        }

        private FrameworkElement BuildLegendVisual(List<LegendItem> items)
        {
            // Ribbon style like your screenshot
            var root = new System.Windows.Controls.Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(6),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var row = (StackPanel)root.Child;

            row.Children.Add(new TextBlock
            {
                Text = "Legend:",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });

            foreach (var it in items)
            {
                // color chip
                row.Children.Add(new System.Windows.Controls.Border
                {
                    Width = 34,
                    Height = 8,
                    Background = it.Brush,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(0.5),
                    CornerRadius = new CornerRadius(2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                });

                // label
                row.Children.Add(new TextBlock
                {
                    Text = it.Label,
                    FontSize = 14,
                    Foreground = Brushes.Black,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 14, 0)
                });
            }

            return root;
        }

        private string MakeLegendLabel(BarrierItemViewModel b)
        {
            // Lead: use your existing commercial gauge label (already like 1/32")
            bool isLead =
                b.MaterialName?.IndexOf("lead", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(b.MaterialName, "Pb", StringComparison.OrdinalIgnoreCase);

            if (isLead)
                return $"{b.CommercialGaugeOnly} Lead";

            // Non-lead: show common fractions (5/8") if close, otherwise decimal inches
            double inches = b.RequiredThickness_mm / 25.4;
            string inchText = ToCommonFractionInches(inches);

            string mat = NormalizeMaterialForLegend(b.MaterialName);
            return $"{inchText} {mat}";
        }

        private string NormalizeMaterialForLegend(string? materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName))
                return "Material";

            if (materialName.Contains("gypsum", StringComparison.OrdinalIgnoreCase) ||
                materialName.Contains("gwb", StringComparison.OrdinalIgnoreCase) ||
                materialName.Contains("drywall", StringComparison.OrdinalIgnoreCase))
                return "Gypsum";

            if (materialName.Contains("steel", StringComparison.OrdinalIgnoreCase))
                return "Steel";

            if (materialName.Contains("concrete", StringComparison.OrdinalIgnoreCase))
                return "Concrete";

            return materialName;
        }


        private string ToCommonFractionInches(double inches)
        {
            // Snap to common denominators so you get 5/8", 1/2", 3/4", etc.
            // If not close, return decimal like 0.63"
            int[] denoms = { 2, 4, 8, 16 };

            double bestErr = double.MaxValue;
            int bestN = 0, bestD = 1;

            foreach (int d in denoms)
            {
                int n = (int)Math.Round(inches * d);
                double v = (double)n / d;
                double err = Math.Abs(v - inches);

                if (err < bestErr)
                {
                    bestErr = err;
                    bestN = n;
                    bestD = d;
                }
            }

            // tolerance: within ~0.02" use fraction
            if (bestErr <= 0.02)
            {
                // reduce fraction
                int g = Gcd(bestN, bestD);
                bestN /= g; bestD /= g;

                // whole inches?
                int whole = bestN / bestD;
                int rem = bestN % bestD;

                if (rem == 0)
                    return $"{whole}\"";

                if (whole == 0)
                    return $"{rem}/{bestD}\"";

                return $"{whole} {rem}/{bestD}\"";
            }

            return $"{inches:0.###}\"";
        }

        private int Gcd(int a, int b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }
            return a == 0 ? 1 : a;
        }

        private (byte[] png, int pxW, int pxH) CaptureLegendPng(IEnumerable<BarrierItemViewModel> barriers, double dpi = 96)
        {
            var items = BuildLegendItems(barriers).ToList();
            if (items.Count == 0) return (Array.Empty<byte>(), 0, 0);

            var legend = BuildLegendVisual(items);

            // Important: measure/arrange because this visual is NOT in the visual tree
            legend.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            legend.Arrange(new Rect(0, 0, legend.DesiredSize.Width, legend.DesiredSize.Height));
            legend.UpdateLayout();

            // Capture without scroll-extents
            var (png0, w0, h0) = CaptureElementPng(legend, dpi, captureFullScrollable: false);

            // Optional: trim any transparent edges (usually minimal with our padding)
            var (png, w, h) = TrimPngWhitespace(png0);
            return (png, w, h);
        }

        private void MenuExport_Click(object sender, RoutedEventArgs e)
        {
            OpenExportDialog();
        }

        private void SaveReport_Click(object sender, RoutedEventArgs e)
        {
            OpenExportDialog();
        }

        private void OpenExportDialog()
        {
            var win = new ExportReportWindow
            {
                Owner = this,
                DataContext = this.DataContext  // MainViewModel bindings
            };
            win.ShowDialog();
        }

        private void AppendReportHeader(Body body, MainViewModel? vm, string roomType, string roomName)
        {
            // Header table: left = institution block, right = date/facility
            var t = new Table(
                new TableProperties(
                    new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                    new TableBorders(
                        new TopBorder { Val = BorderValues.None },
                        new LeftBorder { Val = BorderValues.None },
                        new BottomBorder { Val = BorderValues.None },
                        new RightBorder { Val = BorderValues.None },
                        new InsideHorizontalBorder { Val = BorderValues.None },
                        new InsideVerticalBorder { Val = BorderValues.None }
                    )));

          //  TableCell Cell(params Paragraph[] ps) => new TableCell(ps);
            Paragraph P(string txt, bool bold = false, bool right = false)
            {
                var rPr = new RunProperties(new FontSize { Val = "22" }); // 11pt
                if (bold) rPr.Append(new Bold());
                return new Paragraph(
                    new Run(rPr, new Text(txt ?? "")))
                {
                    ParagraphProperties = new ParagraphProperties(
                        new Justification { Val = right ? JustificationValues.Right : JustificationValues.Left })
                };
            }

            var leftLines = new[]
            {
        vm?.InstitutionName,
        vm?.DepartmentLine1,
        vm?.DepartmentLine2,
        vm?.AddressLine1,
        vm?.AddressLine2,
        vm?.PhoneLine
    };

            var rightLines = new[]
            {
        $"Date: {DateTime.Now:MMMM d, yyyy}",
        string.IsNullOrWhiteSpace(vm?.FacilityName) ? null : $"Facility: {vm!.FacilityName}",
        string.IsNullOrWhiteSpace(vm?.FacilityAddress) ? null : vm!.FacilityAddress
    };

            var leftParas = leftLines.Where(s => !string.IsNullOrWhiteSpace(s))
                                     .Select(s => P(s!, bold: false, right: false)).ToArray();

            var rightParas = rightLines.Where(s => !string.IsNullOrWhiteSpace(s))
                                       .Select(s => P(s!, bold: false, right: true)).ToArray();

            var leftCell = new TableCell(
    new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "3000" })
);
            if (leftParas.Length == 0) leftCell.Append(P(""));
            else leftCell.Append(leftParas);

            var rightCell = new TableCell(
                new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "2000" })
            );
            if (rightParas.Length == 0) rightCell.Append(P(""));
            else rightCell.Append(rightParas);

            var row = new TableRow(leftCell, rightCell);
            t.Append(row);
                     
            body.Append(t);

            // Title
            body.Append(new Paragraph(new Run(new Text(""))));

            var title = new Paragraph(new Run(new RunProperties(new Bold(), new FontSize { Val = "32" }),
                new Text($"Radiation Shielding Specification — {roomType}")))
            {
                ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Center })
            };
            body.Append(title);

            var sub = new Paragraph(new Run(new RunProperties(new FontSize { Val = "24" }),
                new Text($"Room: {roomName}")))
            {
                ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Center })
            };
            body.Append(sub);

            body.Append(new Paragraph(new Run(new Text(""))));
        }

        private void AppendSectionHeading(Body body, string text)
        {
            body.Append(new Paragraph(
                new Run(new RunProperties(new Bold(), new FontSize { Val = "26" }), new Text(text)))
            {
                ParagraphProperties = new ParagraphProperties(
                    new SpacingBetweenLines { Before = "220", After = "120" })
            });
        }

        private void AppendBodyParagraph(Body body, string text)
        {
            body.Append(new Paragraph(
                new Run(new RunProperties(new FontSize { Val = "22" }), new Text(text)))
            {
                ParagraphProperties = new ParagraphProperties(
                    new SpacingBetweenLines { After = "140" })
            });
        }

        private void AppendAssumptions(Body body, MainViewModel? vm)
        {
            void Bullet(string s)
            {
                body.Append(new Paragraph(new Run(new RunProperties(new FontSize { Val = "22" }), new Text("• " + s)))
                {
                    ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines { After = "80" })
                });
            }

            Bullet($"Room category: {vm?.SelectedRoomCategory ?? "—"}");
            Bullet($"Patients/week: {vm?.PatientsPerWeek.ToString("G") ?? "—"}");

            // Add more later (CT/PET/workload mode) once you decide the exact phrasing
            Bullet("Results are based on user-entered geometry, distances, occupancy, and selected calculation methods.");
            Bullet("Verify final shielding design with site-specific construction details and any applicable local regulations.");
        }

        
        private static (byte[] png, int pxW, int pxH) TrimPngWhitespace(byte[] pngBytes, int tol = 12)
        {
            try
            {
                using var ms = new MemoryStream(pngBytes);
                var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var src = decoder.Frames[0];

                int w = src.PixelWidth, h = src.PixelHeight;
                if (w <= 2 || h <= 2) return (pngBytes, w, h);

                var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

                int stride = w * 4;
                byte[] px = new byte[stride * h];
                conv.CopyPixels(px, stride, 0);

                // sample background from 4 corners, average
                (int r, int g, int b) Sample(int x, int y)
                {
                    int idx = y * stride + x * 4;
                    return (px[idx + 2], px[idx + 1], px[idx + 0]);
                }

                var c1 = Sample(0, 0);
                var c2 = Sample(w - 1, 0);
                var c3 = Sample(0, h - 1);
                var c4 = Sample(w - 1, h - 1);

                int br = (c1.r + c2.r + c3.r + c4.r) / 4;
                int bg = (c1.g + c2.g + c3.g + c4.g) / 4;
                int bb = (c1.b + c2.b + c3.b + c4.b) / 4;

                bool IsBg(int idx)
                {
                    byte b = px[idx + 0], g = px[idx + 1], r = px[idx + 2], a = px[idx + 3];
                    if (a < 10) return true; // transparent = whitespace
                    return Math.Abs(r - br) <= tol && Math.Abs(g - bg) <= tol && Math.Abs(b - bb) <= tol;
                }

                int top = 0, bottom = h - 1, left = 0, right = w - 1;

                for (; top < h; top++)
                {
                    bool any = false;
                    for (int x = 0; x < w; x++)
                        if (!IsBg(top * stride + x * 4)) { any = true; break; }
                    if (any) break;
                }

                for (; bottom >= top; bottom--)
                {
                    bool any = false;
                    for (int x = 0; x < w; x++)
                        if (!IsBg(bottom * stride + x * 4)) { any = true; break; }
                    if (any) break;
                }

                for (; left < w; left++)
                {
                    bool any = false;
                    for (int y = top; y <= bottom; y++)
                        if (!IsBg(y * stride + left * 4)) { any = true; break; }
                    if (any) break;
                }

                for (; right >= left; right--)
                {
                    bool any = false;
                    for (int y = top; y <= bottom; y++)
                        if (!IsBg(y * stride + right * 4)) { any = true; break; }
                    if (any) break;
                }

                int nw = right - left + 1;
                int nh = bottom - top + 1;
                if (nw <= 1 || nh <= 1) return (pngBytes, w, h);

                var cropped = new CroppedBitmap(conv, new Int32Rect(left, top, nw, nh));

                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(cropped));
                using var outMs = new MemoryStream();
                enc.Save(outMs);

                return (outMs.ToArray(), nw, nh);
            }
            catch
            {
                // keep original dims to avoid divide-by-zero downstream
                return (pngBytes, 1, 1);
            }
        }


        public void GenerateWordReport(string fileName)
        {
            var vm = DataContext as MainViewModel;

            string roomType = vm?.SelectedRoomCategory ?? "—";
            string roomName = vm?.RoomName ?? "—";

            const double dpi = 288;

            (byte[] pngCanvas, int cw, int ch, byte[] pngLegend, int lw, int lh) capture;

            try
            {
                if (vm != null) vm.IsExporting = true;
                this.UpdateLayout();

                capture = WithSelectionHidden(() =>
                {
                    var region = GetCanvasContentBounds();
                    region = InflateAndClampRegion(region, DesignCanvas, padDip: 15);
                    //FrameworkElement listContent = FindItemsHost(BarrierList) ?? BarrierList;
                    //var (pngSummary0, sw0, sh0) = CaptureElementPng(listContent, dpi, captureFullScrollable: true);
                    //var (pngSummary, sw, sh) = TrimPngWhitespace(pngSummary0);
                    IEnumerable<BarrierItemViewModel> legendSource =
    vm?.Barriers ?? Enumerable.Empty<BarrierItemViewModel>();

                    var (pngLegend, lw, lh) = CaptureLegendPng(legendSource, dpi);

                    double originalOpacity = VM.BackgroundOpacity;

                    try
                    {
                        // Slightly boost background visibility for export
                        VM.BackgroundOpacity = Math.Min(1.0, originalOpacity * 1.6);

                        DesignCanvas.UpdateLayout();

                        var (pngCanvas, cw, ch) =
                            CaptureElementRegionPng(DesignCanvas, region, dpi);

                        return (pngCanvas, cw, ch, pngLegend, lw, lh);
                    }
                    finally
                    {
                        VM.BackgroundOpacity = originalOpacity;
                    }


                 
                });
            }
            finally
            {
                if (vm != null) vm.IsExporting = false;
                BarrierListPanel?.InvalidateMeasure();
                BarrierListPanel?.InvalidateArrange();
                RightPanelScroll?.InvalidateMeasure();
                RightPanelScroll?.InvalidateArrange();
                this.UpdateLayout();

                if (RightPanelScroll != null)
                {
                    RightPanelScroll.ScrollToHorizontalOffset(_savedRightPanelH);
                    RightPanelScroll.ScrollToVerticalOffset(_savedRightPanelV);
                }
            }

            var (pngCanvas, cw, ch, pngLegend, lw, lh) = capture;

            using var mem = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(mem, WordprocessingDocumentType.Document, autoSave: false))
            {
                doc.AddMainDocumentPart();
                doc.MainDocumentPart!.Document = new Document(new Body());
                ConfigurePageToLetterPortrait(doc);

                const double contentWidthInches = 8.5 - 1.0 - 1.0; // 1" margins
                var body = doc.MainDocumentPart!.Document.Body!;

                // --- PROFESSIONAL STRUCTURE ---
                AppendReportHeader(body, vm, roomType, roomName);
                AppendSectionHeading(body, "Design Basis");
                AppendBodyParagraph(body,
                    "Shielding design goals and calculation methodology are based on NCRP guidance and standard medical imaging shielding practices. " +
                    "This report summarizes assumptions and computed barrier requirements for the room configuration and workloads provided.");

                AppendSectionHeading(body, "Summary of Results");
                var resultsTable = MakeResultsTable(vm?.Barriers ?? new ObservableCollection<BarrierItemViewModel>());

                if (vm != null && vm.Barriers is { Count: > 0 })
                {
                    // Optional: switch behavior
                     ApplyTableAutoFit(resultsTable, TableAutoFitMode.ToWindow);
                     ApplyTableAutoFit(resultsTable, TableAutoFitMode.ToContent);

                    body.Append(resultsTable);
                }

                // Page break then figures
                body.Append(new Paragraph(new Run(new Break() { Type = BreakValues.Page })));

                AppendSectionHeading(body, "Room Layout");
                if (pngCanvas.Length > 0 && cw > 0 && ch > 0)
                    body.Append(BuildImageParagraph(doc, pngCanvas, cw, ch, contentWidthInches, dpi, maxHeightInches: 10.0));

                AppendSectionHeading(body, "Legend");
                double legendNaturalWidthIn = lw / dpi;                 // pixels -> inches at capture dpi
                double legendWidthIn = Math.Min(legendNaturalWidthIn, 6.0); // cap (tune if you want)
                if (pngLegend.Length > 0 && lw > 0 && lh > 0)
                    body.Append(BuildImageParagraph(doc, pngLegend, lw, lh, legendWidthIn, dpi, maxHeightInches: 1.2));

                // Notes + signature
                //  body.Append(new Paragraph(new Run(new Break() { Type = BreakValues.Page })));

                AppendSectionHeading(body, "Notes and Assumptions");
                AppendAssumptions(body, vm);

                AppendSectionHeading(body, "Shielding Design Considerations");
                AppendBodyParagraph(body,
                    "Shielding effectiveness may be reduced by gaps, penetrations, back-to-back boxes, seams, unshielded joints, and unprotected ducts. " +
                    "All lead/steel installations should ensure continuous overlap and integrity at all joints, corners, and penetrations.");

                AppendSignatureBlock(doc, body, vm);

                doc.MainDocumentPart.Document.Save();
            }

            File.WriteAllBytes(fileName, mem.ToArray());
        }

        private void AppendSignatureBlock(WordprocessingDocument doc, Body body, MainViewModel? vm)
        {
            body.Append(new Paragraph(new Run(new Text(""))));
            body.Append(new Paragraph(new Run(new Text(""))));

            // Optional signature image
            if (vm?.SignatureImage is BitmapSource bs)
            {
                try
                {
                    var png = BitmapSourceToPngBytes(bs);
                    body.Append(BuildImageParagraph(doc, png, bs.PixelWidth, bs.PixelHeight, contentWidthInches: 1.0, srcDpi: 96, maxHeightInches: 1.5));
                   // InsertImage(doc, png, bs.PixelWidth, bs.PixelHeight, contentWidthInches: 3.0, srcDpi: 96, maxHeightInches: 7.5);
                }
                catch { /* ok to skip */ }
            }

            string line1 = "";
            if (!string.IsNullOrWhiteSpace(vm?.PhysicistName))
            {
                line1 = vm.PhysicistName.Trim();
                if (!string.IsNullOrWhiteSpace(vm.PhysicistCredentials))
                    line1 += ", " + vm.PhysicistCredentials.Trim();
            }

            // Line 2: Title
            string line2 = vm?.PhysicistTitle?.Trim() ?? "";

            // Line 3: License
            string line3 = vm?.LicenseLine?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(line1)) AppendBodyParagraph(body, line1);
            if (!string.IsNullOrWhiteSpace(line2)) AppendBodyParagraph(body, line2);
            if (!string.IsNullOrWhiteSpace(line3)) AppendBodyParagraph(body, line3);

        }

        private static byte[] BitmapSourceToPngBytes(BitmapSource src)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }


        // ---- Background import standardization ----

        // Downsample only when the source is "huge"
        private const int BackgroundDownsampleThresholdPx = 3200; // if max(w,h) > this, downsample
        private const int BackgroundDownsampleMaxPx = 2400;       // decode so max(w,h) ~= this

        // Fit imported background to the canvas nicely
        private const double BackgroundAutoFitPaddingFrac = 0.80; // 95% of canvas

        private static BitmapSource LoadBackgroundBitmapFromFile(
            string filePath,
            out int srcW, out int srcH,
            out int decW, out int decH,
            out double scaleCompensation)
        {
            // Read original size without fully decoding
            using var fsInfo = File.OpenRead(filePath);
            var decInfo = BitmapDecoder.Create(
                fsInfo,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.None);

            var frame = decInfo.Frames[0];
            srcW = frame.PixelWidth;
            srcH = frame.PixelHeight;

            int maxDim = Math.Max(srcW, srcH);

            int? decodePixelWidth = null;
            int? decodePixelHeight = null;

            if (maxDim > BackgroundDownsampleThresholdPx)
            {
                // preserve aspect ratio: set only one decode dimension
                if (srcW >= srcH)
                    decodePixelWidth = BackgroundDownsampleMaxPx;
                else
                    decodePixelHeight = BackgroundDownsampleMaxPx;
            }

            // Now actually decode with optional downsample
            using var fs = File.OpenRead(filePath);

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.PreservePixelFormat;

            bi.StreamSource = fs;

            if (decodePixelWidth.HasValue) bi.DecodePixelWidth = decodePixelWidth.Value;
            if (decodePixelHeight.HasValue) bi.DecodePixelHeight = decodePixelHeight.Value;

            bi.EndInit();
            bi.Freeze();

            decW = bi.PixelWidth;
            decH = bi.PixelHeight;

            // If we downsampled, scale compensation makes the image appear like original
            // (used mainly for project-load; for import we auto-fit anyway)
            scaleCompensation = (decW > 0 && decH > 0)
                ? (double)Math.Max(srcW, srcH) / Math.Max(decW, decH)
                : 1.0;

            return bi;
        }

        private void AutoFitBackgroundToCanvas(int imgW, int imgH)
        {
            if (imgW <= 0 || imgH <= 0) return;

            // Prefer the visible viewport size (what user actually sees)
            double hostW = (DesignCanvas?.ActualWidth > 1) ? DesignCanvas.ActualWidth : VM.CanvasWidth;
            double hostH = (DesignCanvas?.ActualHeight > 1) ? DesignCanvas.ActualHeight : VM.CanvasHeight;

            // If we’re inside a ScrollViewer, use its viewport size (best)
            var sv = FindAncestorScrollViewer(DesignCanvas);
            if (sv != null)
            {
                if (sv.ViewportWidth > 1) hostW = sv.ViewportWidth;
                if (sv.ViewportHeight > 1) hostH = sv.ViewportHeight;
            }

            double targetW = hostW * BackgroundAutoFitPaddingFrac;
            double targetH = hostH * BackgroundAutoFitPaddingFrac;

            const double targetMaxDip = 900; // pick what feels right

            double s = targetMaxDip / Math.Max(imgW, imgH);
            //double s = Math.Min(targetW / imgW, targetH / imgH);
            if (double.IsNaN(s) || double.IsInfinity(s) || s <= 0) s = 1.0;

            VM.BackgroundScale = s;

            // Center within the visible area (viewport)
            VM.BackgroundOffsetX = (hostW - (imgW * s)) * 0.5;
            VM.BackgroundOffsetY = (hostH - (imgH * s)) * 0.5;

            // Also ensure the viewport scrolls to the centered image (important)
            if (sv != null)
            {
                // center of background in canvas coordinates
                double cx = VM.BackgroundOffsetX + (imgW * s) * 0.5;
                double cy = VM.BackgroundOffsetY + (imgH * s) * 0.5;

                // scroll so that center lands in middle of viewport
                double sx = Math.Max(0, cx - sv.ViewportWidth * 0.5);
                double sy = Math.Max(0, cy - sv.ViewportHeight * 0.5);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    sv.ScrollToHorizontalOffset(sx);
                    sv.ScrollToVerticalOffset(sy);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        // import
        private void ImportBackground_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff",
                Title = "Select room layout image"
            };
            if (dlg.ShowDialog(this) == true)
            {
                int srcW, srcH, decW, decH;
                double scaleComp;

                var bmp = LoadBackgroundBitmapFromFile(
                    dlg.FileName,
                    out srcW, out srcH,
                    out decW, out decH,
                    out scaleComp);

                VM.BackgroundImage = bmp;

                // sensible defaults
                VM.BackgroundOpacity = 0.35;

                // Option A: auto-fit to the canvas so users don't have to zoom out a ton
                AutoFitBackgroundToCanvas(decW, decH);
            }
        }

        // zoom buttons
        private void BgZoomIn_Click(object sender, RoutedEventArgs e) => VM.BackgroundScale *= 1.10;
        private void BgZoomOut_Click(object sender, RoutedEventArgs e) => VM.BackgroundScale /= 1.10;


        private bool IsTypingInTextBox()
        {
            // don’t hijack arrows when user is typing in any TextBox
            if (Keyboard.FocusedElement is DependencyObject d)
                return d is TextBox || d is PasswordBox;
            return false;
        }

        private void UpdateDistanceForBarrier(BarrierItemViewModel b)
        {
            // keep your existing distance rules behavior
            var rule = VM.GetRuleDistance_m(b);
            if (rule.HasValue)
            {
                b.DisplayDistance_m = rule.Value;
                b.AutoDistance_m = rule.Value;
                return;
            }

            if (b.DistanceOverride_m > 0) return;

            if (b.SaveDistance && b.SavedRawDistance_m > 0)
            {
                b.DisplayDistance_m = b.SavedRawDistance_m;
                b.AutoDistance_m = b.SavedRawDistance_m;// + b.ExtraOffset_m;
                return;
            }

            var raw = VM.ComputeCanvasDistance_m(b);
            b.DisplayDistance_m = raw;
            b.AutoDistance_m = raw;// + b.ExtraOffset_m;
        }

        private void MoveSelectedBarrier(double dx, double dy)
        {
            var b = VM.SelectedBarrier;
            if (b is null || !b.IsDrawable) return;

            b.X1 += dx; b.Y1 += dy;
            b.X2 += dx; b.Y2 += dy;

            // keep label with barrier (optional but feels right)
          //  b.LabelX += dx;
          //  b.LabelY += dy;

            UpdateDistanceForBarrier(b);
        }

        private void StraightenSelectedBarrierToAxis()
        {
            var b = VM.SelectedBarrier;
            if (b is null || !b.IsDrawable) return;

            double dx = b.X2 - b.X1;
            double dy = b.Y2 - b.Y1;

            // snap to nearest axis
            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                // horizontal
                b.Y2 = b.Y1;
            }
            else
            {
                // vertical
                b.X2 = b.X1;
            }

            UpdateDistanceForBarrier(b);
        }
      
        private void MenuSpectSuite_Click(object sender, RoutedEventArgs e)
        {
            new SpectSuiteWindow { Owner = this }.Show();
        }

        private void MenuTips_Click(object sender, RoutedEventArgs e)
        {
            var w = new TipsWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            w.ShowDialog();
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            var w = new AboutWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            w.ShowDialog();
        }

        private void MenuSwitchRoom_Click(object sender, RoutedEventArgs e)
        {
            if (!PromptSaveIfDirty())
                return;

            VM.ClearDesignCommand.Execute(null);
            VM.CurrentProjectPath = null;
            VM.ClearDirty();
        }


        private void MenuOpen_Click(object sender, RoutedEventArgs e)
        {
            if (!PromptSaveIfDirty()) return;

            var dlg = new OpenFileDialog
            {
                Filter = "Shielding projects (*.rsd.json)|*.rsd.json|JSON (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                ProjectIO.LoadInto(VM, dlg.FileName);
                VM.CurrentProjectPath = dlg.FileName;
                VM.ClearDirty();
                VM.HasActiveDesign = true;     // if your UI uses this to show canvas
                DesignTabItem.IsSelected = true;
                Keyboard.Focus(DesignCanvas);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Open failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(VM.CurrentProjectPath))
            {
                MenuSaveAs_Click(sender, e);
                return;
            }
            try
            {
                ProjectIO.Save(VM, VM.CurrentProjectPath);
                VM.ClearDirty();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuSaveAs_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Shielding projects (*.rsd.json)|*.rsd.json|JSON (*.json)|*.json",
                FileName = string.IsNullOrWhiteSpace(VM.RoomName) ? "project.rsd.json" : $"{VM.RoomName}.rsd.json"
            };
            if (dlg.ShowDialog() != true) return;
            try 
            { 
                ProjectIO.Save(VM, dlg.FileName);
                VM.CurrentProjectPath = dlg.FileName;
                VM.ClearDirty();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
}

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!PromptSaveIfDirty())
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }

        private bool PromptSaveIfDirty()
        {
            if (!VM.IsDirty) return true;

            var result = MessageBox.Show(
                "You have unsaved changes. Save them now?",
                "Shielding Designer",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.No) return true;

            // Yes
            if (string.IsNullOrWhiteSpace(VM.CurrentProjectPath))
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "Shielding projects (*.rsd.json)|*.rsd.json|JSON (*.json)|*.json",
                    FileName = string.IsNullOrWhiteSpace(VM.RoomName) ? "project.rsd.json" : $"{VM.RoomName}.rsd.json"
                };
                if (dlg.ShowDialog() != true) return false;

                try
                {
                    ProjectIO.Save(VM, dlg.FileName);
                    VM.CurrentProjectPath = dlg.FileName;
                    VM.ClearDirty();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                return true;
            }
            try
            {
                ProjectIO.Save(VM, VM.CurrentProjectPath);
                VM.ClearDirty();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return true;
        }

       

    }
}
