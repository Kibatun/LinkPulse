using System.Text;
using System.Text.Json;
using LinkPulse.Worker.Data;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LinkPulse.Worker.Services;

public class ClickTrackerService : BackgroundService
{
    private readonly ILogger<ClickTrackerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnection _connection;
    private IChannel? _channel;
    private const string QueueName = "click_tracking_queue";

    public ClickTrackerService(ILogger<ClickTrackerService> logger, IServiceProvider serviceProvider,
        IConnection connection)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _connection = connection;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _channel = await _connection.CreateChannelAsync(null ,cancellationToken);

        await _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);

        _logger.LogInformation("ClickTrackerService is starting.");
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.ThrowIfCancellationRequested();

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var messageString = Encoding.UTF8.GetString(body);
            _logger.LogInformation("Received message: {Message}", messageString);

            try
            {
                var message = JsonSerializer.Deserialize<ClickMessage>(messageString);
                if (message != null)
                {
                    // Новый scope для DbContext, так как он Scoped
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var shortenedUrl = await dbContext.ShortenedUrls
                            .FirstOrDefaultAsync(u => u.Id == message.ShortenedUrlId, stoppingToken);

                        if (shortenedUrl != null)
                        {
                            shortenedUrl.ClickCount++;
                            await dbContext.SaveChangesAsync(stoppingToken);
                            _logger.LogInformation("Updated click count for URL ID: {UrlId}", message.ShortenedUrlId);
                        }
                        else
                        {
                            _logger.LogWarning("URL with ID: {UrlId} not found.", message.ShortenedUrlId);
                        }
                    }
                }

                await _channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken); // Подтверждается обработка сообщения
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message: {Message}", messageString);
                await _channel.BasicNackAsync(ea.DeliveryTag, false, true, stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(
            queue: QueueName, 
            autoAck: false, 
            consumer: consumer, 
            cancellationToken: stoppingToken);
        _logger.LogInformation("Consumer started. Waiting for messages on queue '{QueueName}'.", QueueName);

        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }

    private class ClickMessage
    {
        public Guid ShortenedUrlId { get; set; }
    }
}