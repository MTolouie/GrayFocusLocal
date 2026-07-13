using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows.Media.Imaging;
using Wpf.Helpers;

namespace Wpf.ViewModels
{
    public partial class ResultViewModel : ObservableObject
    {
        private static readonly HttpClient _httpClient = new();
        private const int MaxConcurrentDownloads = 4; 

        public ObservableCollection<PreviewImageItem> Items { get; } = new();

        public ResultViewModel(List<string> imageUrls)
        {
            foreach (var url in imageUrls)
                Items.Add(new PreviewImageItem(url));

            _ = LoadImagesInParallelAsync();
        }

        private async Task LoadImagesInParallelAsync()
        {
            using var throttle = new SemaphoreSlim(MaxConcurrentDownloads);

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
                byte[] bytes = await _httpClient.GetByteArrayAsync(item.Url);

                var bitmap = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                }
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