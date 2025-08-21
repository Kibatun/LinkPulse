using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace LinkPulse.Api.Services;

public class RabbitMqPublisher : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private const string QueueName = "click_tracking_queue";

    private RabbitMqPublisher(IChannel channel, ILogger<RabbitMqPublisher> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public static async Task<RabbitMqPublisher> CreateAsync(IConnection connection, ILogger<RabbitMqPublisher> logger)
    {
        var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        logger.LogInformation("RabbitMQ channel created and queue '{QueueName}' declared", QueueName);
        return new RabbitMqPublisher(channel, logger);
    }


    public async Task PublishClickEvent(Guid shortenedUrlId)
    {
        try
        {
            var message = new { ShortenedUrlId = shortenedUrlId };
            var messageJson = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(messageJson);

            await _channel.BasicPublishAsync(
                exchange: "",
                routingKey: QueueName,
                body: body,
                mandatory: false
            );

            _logger.LogInformation("Successfully published click event for URL ID: {UrlId}", shortenedUrlId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish click event for URL ID: {UrlId}", shortenedUrlId);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_channel is { IsOpen: true })
            {
                await _channel.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while closing RabbitMQ channel");
        }
    }
}