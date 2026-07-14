namespace Wpf.DTOs
{
    /// <summary>
    /// Mirrors the payload dict grayscale_clr.py's process_image() reports —
    /// either through the progress_callback (status == "progress") or as its
    /// own return value (status == "completed").
    ///
    /// CHANGED from the HTTP version: the old streamed JSON nested the final
    /// numbers under a "results" object (progress.Results.SavedPreviewId,
    /// etc.). grayscale_clr.py's "completed" dict puts those same fields
    /// flat on the payload itself, so this DTO is flattened to match —
    /// update anywhere that referenced progress.Results.X.
    /// </summary>
    public class ProcessingProgress
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        // Only populated when Status == "progress"
        public int Step { get; set; }
        public int TotalSteps { get; set; }

        // Only populated when Status == "completed"
        public string? SessionId { get; set; }
        public int ImagePixelsInRange { get; set; }
        public long GlobalTotalPixels { get; set; }
        public string? SavedPreviewId { get; set; }
    }
}