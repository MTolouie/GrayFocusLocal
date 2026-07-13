using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Wpf.Entities;

public class ProcessingProgress
{
    public string Status { get; set; }
    public int Step { get; set; }

    [JsonPropertyName("total_steps")]
    public int TotalSteps { get; set; }

    public string Message { get; set; }
    public ProcessingResult Results { get; set; }
}
