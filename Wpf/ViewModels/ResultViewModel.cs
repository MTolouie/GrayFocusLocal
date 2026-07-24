using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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

        private readonly IImageProcessingService _processingService;

        public ObservableCollection<PreviewImageItem> Items { get; } = new();

        [ObservableProperty] private PreviewImageItem? _hoveredItem;
        private readonly BatchMetadataDTO _metadata;




        public ResultViewModel(BatchMetadataDTO metadata,
    IImageProcessingService processingService)
        {
            _processingService = processingService;
            _metadata = metadata;

            foreach (var (sessionId, previewId) in metadata.PreviewRefs)
            {
                // FIXED: Instead of mapping the previewId to the UI label, map the exact file name
                var item = new PreviewImageItem(sessionId, previewId)
                {
                    Label = previewId
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
            // Process items sequentially on a background thread to avoid GIL lock contention
            await Task.Run(async () =>
            {
                foreach (var item in Items)
                {
                    await LoadSingleImageAsync(item);
                }
            });
        }

        private async Task LoadSingleImageAsync(PreviewImageItem item)
        {
            try
            {
                var data = await _processingService.GetPreviewImageAsync(item.SessionId, item.PreviewId);

                var bitmap = BitmapSource.Create(
                    data.Width, data.Height,
                    96, 96,
                    PixelFormats.Bgr48,
                    null,
                    data.PixelData,
                    data.Stride);
                bitmap.Freeze();

                // Dispatch UI property updates back to the UI Thread
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    item.ImageSource = bitmap;
                    item.ZoomScale = 1.0;
                    item.IsLoading = false;
                });
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    item.IsLoading = false;
                    item.ErrorMessage = $"Failed to load: {ex.Message}";
                });
            }
            finally
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    SaveImagesCommand.NotifyCanExecuteChanged();
                });
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

        // 1. Tell the command to check CanSaveImages to determine if the button is enabled
        [RelayCommand(CanExecute = nameof(CanSaveImages))]
        private async Task SaveImages()
        {
            try
            {
                // 1. Open Folder Picker
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select Destination Folder for Processed Images & Metadata",
                    Multiselect = false
                };

                bool? result = dialog.ShowDialog();
                if (result != true || string.IsNullOrWhiteSpace(dialog.FolderName))
                {
                    return;
                }

                string outputDir = dialog.FolderName;

                int saved = 0;
                int skipped = 0;
                var savedImageNames = new List<string>();

                // 2. Save all images concurrently/sequentially
                await Task.Run(() =>
                {
                    foreach (var item in Items)
                    {
                        if (item.ImageSource is not BitmapSource bitmap)
                        {
                            skipped++;
                            continue;
                        }

                        string safeName = string.IsNullOrWhiteSpace(item.Label)
                            ? item.PreviewId
                            : Path.GetFileNameWithoutExtension(item.Label);

                        string imagePath = Path.Combine(outputDir, $"{safeName}.png");

                        // Collision prevention for duplicate filenames
                        int counter = 1;
                        while (File.Exists(imagePath))
                        {
                            string numberedName = $"{safeName}_{counter++}";
                            imagePath = Path.Combine(outputDir, $"{numberedName}.png");
                        }

                        // Save PNG
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var fs = new FileStream(imagePath, FileMode.Create, FileAccess.Write))
                        {
                            encoder.Save(fs);
                        }

                        saved++;
                        savedImageNames.Add(Path.GetFileName(imagePath));
                    }

                    // 3. Create ONE single metadata report text file for the entire batch
                    if (saved > 0)
                    {
                        string summaryTxtPath = Path.Combine(outputDir, $"batch_summary_{_metadata.SessionId[..8]}.txt");

                        // Collision handling for summary text file
                        int textCounter = 1;
                        while (File.Exists(summaryTxtPath))
                        {
                            summaryTxtPath = Path.Combine(outputDir, $"batch_summary_{_metadata.SessionId[..8]}_{textCounter++}.txt");
                        }

                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("==================================================");
                        sb.AppendLine("           BATCH ANALYSIS METADATA REPORT         ");
                        sb.AppendLine("==================================================");
                        sb.AppendLine($"Session ID:               {_metadata.SessionId}");
                        sb.AppendLine($"Date & Time:              {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        sb.AppendLine($"Saved Images Count:       {saved}");
                        sb.AppendLine("--------------------------------------------------");
                        sb.AppendLine("GEOMETRIC & ACQUISITION METRICS:");
                        sb.AppendLine($"  Object Pixel Size:      {_metadata.ObjectPixelSizeMicrons.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} µm");
                        sb.AppendLine($"  Magnification:          {_metadata.Magnification.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}x");
                        sb.AppendLine($"  FOD (Focus-Object):     {_metadata.FodValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} mm");
                        sb.AppendLine($"  FDD (Focus-Detector):   {_metadata.FddValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} mm");
                        sb.AppendLine($"  Total Pixel Range:      {_metadata.TotalPixelInRange.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
                        sb.AppendLine("--------------------------------------------------");
                        sb.AppendLine("INTENSITY CUTOFF THRESHOLDS:");
                        sb.AppendLine($"  Min Value Cutoff:       {_metadata.MinValue}");
                        sb.AppendLine($"  Max Value Cutoff:       {_metadata.MaxValue}");
                        sb.AppendLine("--------------------------------------------------");
                        sb.AppendLine("INCLUDED IMAGE FILES:");
                        foreach (var fileName in savedImageNames)
                        {
                            sb.AppendLine($"  - {fileName}");
                        }
                        sb.AppendLine("==================================================");

                        File.WriteAllText(summaryTxtPath, sb.ToString());
                    }
                });

                MessageBox.Show(
                    $"Saved {saved} image(s) and 1 batch summary .txt report to:\n{outputDir}" + (skipped > 0 ? $"\n({skipped} not yet loaded, skipped)" : ""),
                    "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save images or metadata summary: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 2. The condition: Returns TRUE only if there is at least 1 item and NONE are loading
        private bool CanSaveImages()
        {
            return Items.Count > 0 && Items.All(item => !item.IsLoading);
        }
    }
}