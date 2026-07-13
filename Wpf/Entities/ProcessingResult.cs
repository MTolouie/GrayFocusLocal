using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Wpf.Entities;

public class ProcessingResult
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; }

    [JsonPropertyName("image_pixels_in_range")]
    public int ImagePixelsInRange { get; set; }

    [JsonPropertyName("global_total_pixels")]
    public int GlobalTotalPixels { get; set; }

    [JsonPropertyName("saved_preview_id")]
    public string SavedPreviewId { get; set; }
}

