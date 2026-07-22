using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media.Imaging;

namespace Wpf.Helpers
{
    /// <summary>
    /// CHANGED: previously took a single download Url. There's no server to
    /// download from anymore, so each item now carries the (sessionId,
    /// previewId) pair needed to call GetPreviewImageAsync in-process.
    /// If your real PreviewImageItem has more members than shown here (it
    /// wasn't part of the files you uploaded), fold this constructor/property
    /// change into your actual file rather than replacing it wholesale.
    /// </summary>
    public partial class PreviewImageItem : ObservableObject
    {
        public string SessionId { get; }
        public string PreviewId { get; }

        public string Label { get; set; }

        [ObservableProperty] private BitmapSource? _imageSource;
        [ObservableProperty] private bool _isLoading = true;
        [ObservableProperty] private string? _errorMessage;

        // --- ZOOM SUPPORT ---
        [ObservableProperty] private double _zoomScale = 1.0;

        public PreviewImageItem(string sessionId, string previewId)
        {
            SessionId = sessionId;
            PreviewId = previewId;
        }
    }
}