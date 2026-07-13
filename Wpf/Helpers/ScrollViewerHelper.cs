using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Wpf.Helpers
{
    public static class ScrollViewerHelper
    {
        public static readonly DependencyProperty IsRightClickPanEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsRightClickPanEnabled",
                typeof(bool),
                typeof(ScrollViewerHelper),
                new PropertyMetadata(false, OnIsRightClickPanEnabledChanged));

        public static bool GetIsRightClickPanEnabled(DependencyObject obj) => (bool)obj.GetValue(IsRightClickPanEnabledProperty);
        public static void SetIsRightClickPanEnabled(DependencyObject obj, bool value) => obj.SetValue(IsRightClickPanEnabledProperty, value);

        private static void OnIsRightClickPanEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                if ((bool)e.NewValue)
                {
                    // Make sure the ScrollViewer is fully allowed to handle tab-stops and keyboard focus
                    scrollViewer.Focusable = true;
                    scrollViewer.IsTabStop = true;

                    // 1. Force keyboard focus to the ScrollViewer whenever the user clicks inside the viewer workspace
                    scrollViewer.PreviewMouseLeftButtonDown += ScrollViewer_PreviewMouseLeftButtonDown;

                    // 2. Intercept arrow keys reliably
                    scrollViewer.PreviewKeyDown += ScrollViewer_PreviewKeyDown;

                    // 3. Set focus automatically as soon as it loads up
                    scrollViewer.Loaded += (s, args) => scrollViewer.Focus();
                }
                else
                {
                    scrollViewer.PreviewMouseLeftButtonDown -= ScrollViewer_PreviewMouseLeftButtonDown;
                    scrollViewer.PreviewKeyDown -= ScrollViewer_PreviewKeyDown;
                }
            }
        }

        private static void ScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                // Instantly grabs keyboard focus when you click to draw/select a shape
                sv.Focus();
                Keyboard.Focus(sv);
            }
        }

        private static void ScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                const double scrollStep = 50.0; // Feel free to adjust your step-panning distance here

                switch (e.Key)
                {
                    case Key.Left:
                        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - scrollStep);
                        e.Handled = true; // Prevents the keypress from triggering default tab item jumps
                        break;
                    case Key.Right:
                        sv.ScrollToHorizontalOffset(sv.HorizontalOffset + scrollStep);
                        e.Handled = true;
                        break;
                    case Key.Up:
                        sv.ScrollToVerticalOffset(sv.VerticalOffset - scrollStep);
                        e.Handled = true;
                        break;
                    case Key.Down:
                        sv.ScrollToVerticalOffset(sv.VerticalOffset + scrollStep);
                        e.Handled = true;
                        break;
                }
            }
        }
    }
}