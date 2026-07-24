public class BatchMetadataDTO
{
    public string SessionId { get; set; } = string.Empty;
    public double FodValue { get; set; }
    public double FddValue { get; set; }
    public double ObjectPixelSizeMicrons { get; set; }
    public double Magnification { get; set; }
    public int MinValue { get; set; }
    public int MaxValue { get; set; }
    public long TotalPixelInRange { get; set; }
    public List<(string SessionId, string PreviewId)> PreviewRefs { get; set; } = new();
}