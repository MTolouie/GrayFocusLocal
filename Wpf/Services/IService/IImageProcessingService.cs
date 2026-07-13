using System;
using System.Collections.Generic;
using System.Text;
using Wpf.DTOs;
using Wpf.Entities;

namespace Wpf.Services.IService;

public interface IImageProcessingService
{
    // We use IAsyncEnumerable because the Python API returns a streaming NDJSON response chunk-by-chunk
    public IAsyncEnumerable<ProcessingProgress> ScanImageAsync(byte[] imageBytes,string sessionId);

    public Task<ResponseDTO> SendDataAsync(ScanRequestDTO request);

    public Task CleanUpDataAsync(string sessionId);
}
