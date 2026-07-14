namespace Wpf.DTOs
{
    /// <summary>
    /// Generic status wrapper. Previously deserialized from the API's JSON
    /// response body; grayscale_clr.py's start_session()/cleanup_session()
    /// return nothing (or raise on failure), so ImageProcessingService now
    /// constructs this itself instead of parsing JSON.
    /// </summary>
    public class ResponseDTO
    {
        public string status { get; set; } = string.Empty;
        public string? message { get; set; }
    }
}