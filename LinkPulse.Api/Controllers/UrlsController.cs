using LinkPulse.Api.Data;
using LinkPulse.Api.DTO;
using LinkPulse.Api.Entities;
using LinkPulse.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LinkPulse.Api.Controllers;

[ApiController]
[Route("api/urls")]
public class UrlsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public UrlsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
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
            CreatedOnUtc = DateTime.UtcNow
        };

        await _dbContext.ShortenedUrls.AddAsync(shortenedUrl);
        await _dbContext.SaveChangesAsync();

        var response = new
        {
            shortUrl = $"http://localhost:8080/{shortenedUrl.ShortCode}"
        };

        return Ok(response);
    }
}