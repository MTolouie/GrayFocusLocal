using System;
using System.Collections.Generic;
using System.Text;

namespace Wpf.DTOs;

public class ScanRequestDTO
{
    public byte[] ImageBytes { get; set; }
    public string SessionId { get; set; }
    public int MinValue { get; set; }
    public int MaxValue { get; set; }
    public int total_expected_images { get; set; }
    public int preview_count { get; set; }
}
