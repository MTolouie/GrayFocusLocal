using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Helpers;
using Wpf.Services.IService;

namespace Wpf.ViewModels
{
    /// <summary>
    /// Full rewrite: no HttpClient, no GetByteArrayAsync, no download URLs.
    /// Preview images now come straight out of the same in-process Python
    /// engine via IImageProcessingService.GetPreviewImageAsync, decoded
    /// directly into a BitmapSource. The concurrency cap is kept (each call
    /// still blocks on the GIL even though there's no network latency).
    /// </summary>
    public partial class ResultViewModel : ObservableObject
    {
        private const int MaxConcurrentLoads = 4;

        private readonly IImageProcessingService _processingService;

        public ObservableCollection<PreviewImageItem> Items { get; } = new();

        [ObservableProperty] private PreviewImageItem? _hoveredItem;

        public ResultViewModel(
    List<(string SessionId, string PreviewId, string FileName)> previewRefs,
    IImageProcessingService processingService)
        {
            _processingService = processingService;

            foreach (var (sessionId, previewId, fileName) in previewRefs)
            {
                // FIXED: Instead of mapping the previewId to the UI label, map the exact file name
                var item = new PreviewImageItem(sessionId, previewId)
                {
                    Label = fileName
                };
                Items.Add(item);
            }

            _ = LoadImagesInParallelAsync();
        }

        [RelayCommand]
        private void SetHoveredItem(PreviewImageItem item)
        {
            HoveredItem = item;
        }

        private async Task LoadImagesInParallelAsync()
        {
            using var throttle = new SemaphoreSlim(MaxConcurrentLoads);

            var tasks = Items.Select(async item =>
            {
                await throttle.WaitAsync();
                try
                {
                    await LoadSingleImageAsync(item);
                }
                finally
                {
                    throttle.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        private async Task LoadSingleImageAsync(PreviewImageItem item)
        {
            try
            {
                var data = await _processingService.GetPreviewImageAsync(item.SessionId, item.PreviewId);

                // CHANGED: Use PixelFormats.Gray16 for native 16-bit grayscale buffer
                var bitmap = BitmapSource.Create(
                    data.Width, data.Height,
                    96, 96,
                    PixelFormats.Rgb48, // <--- Formerly PixelFormats.Bgr24
                    null,
                    data.PixelData,
                    data.Stride);
                bitmap.Freeze();

                item.ImageSource = bitmap;
                item.ZoomScale = 1.0;
                item.IsLoading = false;
            }
            catch (Exception ex)
            {
                item.IsLoading = false;
                item.ErrorMessage = $"Failed to load: {ex.Message}";
            }
        }

        [RelayCommand]
        private void WindowKeyDown(KeyEventArgs e)
        {
            if (e == null || HoveredItem == null) return;

            // Determine move step size (adjust speed if needed)
            double panOffset = 40.0;

            // Find the ScrollViewer belonging to the currently hovered item
            if (e.Source is Window win)
            {
                var scrollViewer = FindScrollViewerForHoveredItem(win, HoveredItem);
                if (scrollViewer == null) return;

                switch (e.Key)
                {
                    case Key.Left:
                        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - panOffset);
                        e.Handled = true;
                        break;
                    case Key.Right:
                        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + panOffset);
                        e.Handled = true;
                        break;
                    case Key.Up:
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - panOffset);
                        e.Handled = true;
                        break;
                    case Key.Down:
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + panOffset);
                        e.Handled = true;
                        break;
                }
            }
        }

        private ScrollViewer? FindScrollViewerForHoveredItem(DependencyObject parent, PreviewImageItem targetItem)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is ScrollViewer sv && sv.DataContext == targetItem && sv.Name == "ImageScrollViewer")
                {
                    return sv;
                }

                var result = FindScrollViewerForHoveredItem(child, targetItem);
                if (result != null) return result;
            }
            return null;
        }

        [RelayCommand]
        private void ImageMouseWheel(MouseWheelEventArgs e)
        {
            if (e == null) return;

            if ((e.Source as FrameworkElement)?.DataContext is not PreviewImageItem item)
                return;

            ScrollViewer? scrollViewer = null;
            DependencyObject current = e.Source as DependencyObject;
            while (current != null)
            {
                if (current is ScrollViewer sv && sv.Name == "ImageScrollViewer")
                {
                    scrollViewer = sv;
                    break;
                }
                current = VisualTreeHelper.GetParent(current);
            }

            if (scrollViewer == null) return;

            Point mousePosView = e.GetPosition(scrollViewer);

            double mouseInContentX = scrollViewer.HorizontalOffset + mousePosView.X;
            double mouseInContentY = scrollViewer.VerticalOffset + mousePosView.Y;

            double oldScale = item.ZoomScale;

            if (e.Delta > 0)
            {
                if (item.ZoomScale < 50.0) item.ZoomScale += 0.5;
            }
            else
            {
                // Don't zoom out past 1.0 so image never shrinks into oblivion
                if (item.ZoomScale > 1.0) item.ZoomScale -= 0.5;
            }

            // Reset scroll offsets to top-left if returned to default zoom
            if (item.ZoomScale <= 1.0)
            {
                scrollViewer.ScrollToHorizontalOffset(0);
                scrollViewer.ScrollToVerticalOffset(0);
            }
            else
            {
                double scaleRatio = item.ZoomScale / oldScale;
                double newScrollX = (mouseInContentX * scaleRatio) - mousePosView.X;
                double newScrollY = (mouseInContentY * scaleRatio) - mousePosView.Y;

                scrollViewer.ScrollToHorizontalOffset(newScrollX);
                scrollViewer.ScrollToVerticalOffset(newScrollY);
            }

            e.Handled = true;
        }
    }
}