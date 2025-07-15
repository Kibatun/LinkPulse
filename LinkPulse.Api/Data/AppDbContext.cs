using LinkPulse.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace LinkPulse.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ShortenedUrl> ShortenedUrls { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortenedUrl>(builder => { builder.HasIndex(x => x.Id).IsUnique(); });
    }
}