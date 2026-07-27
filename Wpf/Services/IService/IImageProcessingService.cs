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

        Task<PreviewImageData> GetPreviewImageAsync(string sessionId, string previewId, CancellationToken cancellationToken = default);

        Task CleanUpDataAsync(string sessionId);

        Task<SessionResultsDTO> ProcessFolderAsync(string sessionId, string folderPath, Action<ProcessingProgress> progressReporter);
    }
}