using LinkPulse.Api.DTO;
using LinkPulse.Api.Services;
using LinkPulse.Core.Data;
using LinkPulse.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LinkPulse.Api.Controllers;

[ApiController]
[Route("api/urls")]
public class UrlsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly RabbitMqPublisher _rabbitMqPublisher;
    private readonly ILogger<UrlsController> _logger;

    public UrlsController(AppDbContext dbContext, RabbitMqPublisher rabbitMqPublisher, ILogger<UrlsController> logger)
    {
        _dbContext = dbContext;
        _rabbitMqPublisher = rabbitMqPublisher;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> ShortenUrl([FromBody] ShortenUrlRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
            return BadRequest("The specified URL is not valid.");

        var shortCode = ShortCodeGenerator.Generate();

        var shortenedUrl = new ShortenedUrl
        {
            Id = Guid.NewGuid(),
            LongUrl = request.Url,
            ShortCode = shortCode,
            CreatedOnUtc = DateTime.UtcNow,
            ClickCount = 0
        };

        await _dbContext.ShortenedUrls.AddAsync(shortenedUrl);
        await _dbContext.SaveChangesAsync();

        var response = new
        {
            shortUrl = $"http://localhost:8080/a/{shortenedUrl.ShortCode}"
        };

        return Ok(response);
    }

    [HttpGet("/a/{shortCode}")]
    public async Task<IActionResult> RedirectToLongUrl(string shortCode)
    {
        try
        {
            var shortenedUrl = await _dbContext.ShortenedUrls
                .FirstOrDefaultAsync(u => u.ShortCode == shortCode);

            if (shortenedUrl == null)
            {
                _logger.LogWarning("Short code not found: {ShortCode}", shortCode);
                return NotFound();
            }

            await _rabbitMqPublisher.PublishClickEvent(shortenedUrl.Id);
            _logger.LogInformation("Published click event for URL ID: {UrlId}", shortenedUrl.Id);

            return RedirectPermanent(shortenedUrl.LongUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing redirect for short code: {ShortCode}", shortCode);
            return StatusCode(500, "Internal server error");
        }
    }
    
    [HttpGet("{shortCode}/stats")]
    [ProducesResponseType(typeof(UrlStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUrlStats(string shortCode)
    {
        var shortenedUrl = await _dbContext.ShortenedUrls
            .AsNoTracking() 
            .FirstOrDefaultAsync(u => u.ShortCode == shortCode);

        if (shortenedUrl == null)
        {
            return NotFound();
        }

        var shortUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/a/{shortenedUrl.ShortCode}";

        var response = new UrlStatsResponse(
            shortenedUrl.LongUrl,
            shortUrl,
            shortenedUrl.ClickCount
        );

        return Ok(response);
    }
}