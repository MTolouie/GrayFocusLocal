using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media.Imaging;

namespace Wpf.Helpers
{
    public partial class PreviewImageItem : ObservableObject
    {
        public string Url { get; }
        public string Label { get; }

        [ObservableProperty] private BitmapImage? _imageSource;
        [ObservableProperty] private bool _isLoading = true;
        [ObservableProperty] private string? _errorMessage;

        public PreviewImageItem(string url)
        {
            Url = url;
            Label = url;
        }
    }
}
