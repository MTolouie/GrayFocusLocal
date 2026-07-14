using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        public ResultViewModel(
            List<(string SessionId, string PreviewId)> previewRefs,
            IImageProcessingService processingService)
        {
            _processingService = processingService;

            foreach (var (sessionId, previewId) in previewRefs)
                Items.Add(new PreviewImageItem(sessionId, previewId));

            _ = LoadImagesInParallelAsync();
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

                var bitmap = BitmapSource.Create(
                    data.Width, data.Height,
                    96, 96,
                    PixelFormats.Bgr24,
                    null,
                    data.PixelData,
                    data.Stride);
                bitmap.Freeze();

                item.ImageSource = bitmap;
                item.IsLoading = false;
            }
            catch (Exception ex)
            {
                item.IsLoading = false;
                item.ErrorMessage = $"Failed to load: {ex.Message}";
            }
        }
    }
}