namespace LinkPulse.Worker.Entities;

public class ShortenedUrl
{
    public Guid Id { get; set; }
    public string LongUrl { get; set; }
    public string  ShortCode { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    
    public int ClickCount { get; set; }
    
}