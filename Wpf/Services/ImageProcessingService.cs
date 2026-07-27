using System;
using System.Threading.Tasks;
using Python.Runtime;
using Wpf.DTOs;
using Wpf.Services.IService;

namespace Wpf.Services
{
    /// <summary>
    /// Full rewrite: no HttpClient, no JSON, no MultipartFormDataContent, no
    /// NDJSON streaming. Every call goes through PythonEngineService.Processor
    /// (grayscale_clr.GrayscaleProcessor) inside `using (Py.GIL())`, wrapped
    /// in Task.Run since Python.NET calls block the calling thread.
    /// </summary>
    public class ImageProcessingService : IImageProcessingService
    {
        private readonly PythonEngineService _engine;

        public ImageProcessingService(PythonEngineService engine)
        {
            _engine = engine;
        }

        public Task CleanUpDataAsync(string sessionId)
        {
            return Task.Run(() =>
            {
                using (Py.GIL())
                {
                    // Raises KeyError -> PythonException if the session doesn't
                    // exist; let it propagate, same as the old non-"success"
                    // response used to surface as a thrown Exception.
                    _engine.Processor.cleanup_session(sessionId);
                }
            });
        }

        public Task<ResponseDTO> SendDataAsync(ScanRequestDTO request)
        {
            return Task.Run(() =>
            {
                try
                {
                    using (Py.GIL())
                    {
                        // Cleanly re-initialize the processor inside PythonEngineService
                        _engine.ReinitializeProcessor(request.UseGpu);

                        _engine.Processor.start_session(
                            request.SessionId ?? string.Empty,
                            request.MinValue,
                            request.MaxValue,
                            request.total_expected_images,
                            request.preview_count);
                    }

                    return new ResponseDTO { status = "started" };
                }
                catch (PythonException ex)
                {
                    return new ResponseDTO { status = "error", message = ex.Message };
                }
            });
        }

        public Task<SessionResultsDTO> ProcessFolderAsync(string sessionId, string folderPath, Action<ProcessingProgress> progressReporter)
        {
            return Task.Run(() =>
            {
                using (Py.GIL())
                {
                    // 1. Wrap the progress callback to report live steps from parallel workers
                    Action<object> pyCallback = (pyPayload) =>
                    {
                        PyObject pyObj = (PyObject)pyPayload;
                        ProcessingProgress progress = MapProgress(pyObj);
                        progressReporter?.Invoke(progress);
                    };

                    // 2. Call the bulk process method in grayscale_clr.py
                    dynamic resultsDict = _engine.Processor.process_images(sessionId, folderPath, pyCallback);
                    PyObject pyObj = (PyObject)resultsDict;

                    // 3. Parse and return the final SessionResultsDTO directly
                    var results = new SessionResultsDTO();
                    using (PyObject pySessionId = pyObj.GetItem("session_id"))
                    using (PyObject pyProcessedCount = pyObj.GetItem("total_images_processed"))
                    using (PyObject pyGlobalPixels = pyObj.GetItem("global_total_pixels"))
                    using (PyObject pyPeriodicPreviews = pyObj.GetItem("periodic_previews"))
                    {
                        results.SessionId = pySessionId.ToString();
                        results.TotalImagesProcessed = pyProcessedCount.As<int>();
                        results.GlobalTotalPixels = pyGlobalPixels.As<long>();

                        var previewsList = new System.Collections.Generic.List<string>();
                        int listLength = (int)pyPeriodicPreviews.Length();
                        for (int i = 0; i < listLength; i++)
                        {
                            using (PyObject item = pyPeriodicPreviews.GetItem(i))
                            {
                                previewsList.Add(item.ToString());
                            }
                        }
                        results.PeriodicPreviews = previewsList;
                    }

                    return results;
                }
            });
        }

        public Task<PreviewImageData> GetPreviewImageAsync(string sessionId, string previewId, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                // Throw if cancellation was requested before acquiring the Python GIL
                cancellationToken.ThrowIfCancellationRequested();

                using (Py.GIL())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    dynamic img = _engine.Processor.get_image(sessionId, previewId);

                    int height = (int)img.shape[0];
                    int width = (int)img.shape[1];
                    int channels = (int)img.shape[2]; // 3 (RGB)

                    int bytesPerPixel = 2 * channels; // 6 for 16-bit RGB
                    int stride = width * bytesPerPixel;

                    PyObject rawBytes = img.tobytes();
                    byte[] buffer = (byte[])rawBytes.As<byte[]>();

                    return new PreviewImageData
                    {
                        PixelData = buffer,
                        Width = width,
                        Height = height,
                        Stride = stride
                    };
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Replaces JsonSerializer.Deserialize&lt;ProcessingProgress&gt;() —
        /// pulls fields straight off the Python dict via dynamic attribute
        /// access, matching the exact payload shape grayscale_clr.py builds.
        /// </summary>
        private static ProcessingProgress MapProgress(dynamic payload)
        {
            // 1. Ensure we strictly hold the GIL during the conversion!
            // Even if the calling method has it, we wrap it here to be absolutely safe 
            // against compiler-generated dynamic thread-switching.
            using (Py.GIL())
            {
                // 2. Safely cast the dynamic payload to a PyObject
                PyObject pyObj = (PyObject)payload;

                // 3. Extract the status using a explicit direct cast or PyObject's safe item fetch
                using (PyObject pyStatus = pyObj.GetItem("status"))
                {
                    string status = pyStatus.ToString(); // Or pyStatus.As<string>() safely inside this block

                    var progress = new ProcessingProgress
                    {
                        Status = status
                    };

                    if (status == "progress")
                    {
                        using (PyObject pyMessage = pyObj.GetItem("message"))
                        using (PyObject pyStep = pyObj.GetItem("step"))
                        using (PyObject pyTotalSteps = pyObj.GetItem("total_steps"))
                        {
                            progress.Message = pyMessage.ToString();
                            progress.Step = pyStep.As<int>();
                            progress.TotalSteps = pyTotalSteps.As<int>();
                        }
                    }
                    else if (status == "completed")
                    {
                        using (PyObject pySessionId = pyObj.GetItem("session_id"))
                        using (PyObject pyPixelsInRange = pyObj.GetItem("image_pixels_in_range"))
                        using (PyObject pyGlobalPixels = pyObj.GetItem("global_total_pixels"))
                        using (PyObject pySavedPreviewId = pyObj.GetItem("saved_preview_id"))
                        {
                            progress.SessionId = pySessionId.ToString();
                            progress.ImagePixelsInRange = pyPixelsInRange.As<int>();
                            progress.GlobalTotalPixels = pyGlobalPixels.As<long>();
                            progress.SavedPreviewId = pySavedPreviewId.IsNone() ? null : pySavedPreviewId.ToString();
                        }
                    }

                    return progress;
                }


            }
        }

    }
}