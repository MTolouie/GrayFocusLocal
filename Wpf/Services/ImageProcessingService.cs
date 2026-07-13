using Wpf.DTOs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using Wpf.Services.IService;
using Wpf.Entities;

namespace Wpf.Services
{
    public class ImageProcessingService : IImageProcessingService
    {
        private readonly HttpClient _httpClient;

        public ImageProcessingService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }


        public async Task CleanUpDataAsync(string sessionId)
        {
            var response = await _httpClient.DeleteAsync($"http://127.0.0.1:8000/session/{sessionId}/cleanup");

            var data = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ResponseDTO>(data);

            if (result?.status != "success")
                throw new Exception($"Failed to clean up session {sessionId}. Server response: {data}");


        }
        public async Task<ResponseDTO> SendDataAsync(ScanRequestDTO request)
        {
            // 1. Create an anonymous object matching the expected JSON structure
            var payload = new
            {
                session_id = request.SessionId ?? string.Empty,
                min_val = request.MinValue,
                max_val = request.MaxValue,
                total_expected_images = request.total_expected_images, // Make sure this matches your Python model's variable name!
                preview_count = request.preview_count 
            };

            // 2. Serialize to JSON and specify the application/json content type
            string jsonString = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            // 3. Build and send the HttpRequestMessage natively
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:8000/session/start")
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(httpRequest);
            var data = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ResponseDTO>(data);

            

            return result;
        }

        public async IAsyncEnumerable<ProcessingProgress> ScanImageAsync(byte[] imageBytes, string sessionId)
        {
            // --- INVOCATION TRACER LOGGING ---
            // Generate a unique token for THIS specific method execution context
            string methodCallToken = Guid.NewGuid().ToString().Substring(0, 8);

            // 1. Create the multi-part form content with an explicit boundary to guarantee uniqueness
            using var content = new MultipartFormDataContent($"UploadBoundary-{methodCallToken}");

            var fileContent = new ByteArrayContent(imageBytes);
            // Explicitly verify content type matching for medical image handling
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/tiff");

            content.Add(fileContent, "file", "cropped.tif");

            // 2. Build an HttpRequestMessage natively
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:8000/session/{sessionId}/process_image/")
            {
                Content = content
            };

            // 3. SendAsync with stream optimization parameters
            var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            System.Diagnostics.Debug.WriteLine($"[HTTP TRACE - {methodCallToken}] Server handshake established. Status: {response.StatusCode}");

            response.EnsureSuccessStatusCode();

            // 4. Read the streaming response chunks line-by-line
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                ProcessingProgress progress = null;

                try
                {
                    progress = JsonSerializer.Deserialize<ProcessingProgress>(line, options);
                }
                catch (JsonException jsonEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[HTTP TRACE - {methodCallToken}] JSON Parse Warning on line: {line}. Error: {jsonEx.Message}");
                    continue;
                }

                if (progress != null)
                {
                    yield return progress;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[HTTP TRACE - {methodCallToken}] Finished consumption of response stream cleanly.");
        }
    }
}