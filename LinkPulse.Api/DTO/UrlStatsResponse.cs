namespace LinkPulse.Api.DTO;

public record UrlStatsResponse(
    string LongUrl,
    string ShortUrl,
    int ClickCount
);