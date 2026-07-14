using System;
using System.Threading.Tasks;
using Wpf.DTOs;
using Wpf.Entities;

namespace Wpf.Services.IService
{
    /// <summary>
    /// NOTE: this interface's shape changed with the pythonnet migration —
    /// ScanImageAsync (byte[] + IAsyncEnumerable stream) is gone, replaced by
    /// ProcessImageAsync (file path + IProgress callback), and a new
    /// GetPreviewImageAsync replaces the old HTTP preview-image download.
    /// Update your DI registrations accordingly if this differs from your
    /// current interface file.
    /// </summary>
    public interface IImageProcessingService
    {
        Task<ResponseDTO> SendDataAsync(ScanRequestDTO request);

        Task<ProcessingProgress> ProcessImageAsync(string sessionId, string imagePath, int currentIdx, Action<ProcessingProgress> progressReporter);
    

        Task<PreviewImageData> GetPreviewImageAsync(string sessionId, string previewId);

        Task CleanUpDataAsync(string sessionId);
    }
}