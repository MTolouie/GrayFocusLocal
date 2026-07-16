namespace Wpf.DTOs
{
    public class SessionResultsDTO
    {
        public string SessionId { get; set; } = string.Empty;
        public int TotalImagesProcessed { get; set; }
        public long GlobalTotalPixels { get; set; }
        public System.Collections.Generic.List<string> PeriodicPreviews { get; set; } = new();
    }
}