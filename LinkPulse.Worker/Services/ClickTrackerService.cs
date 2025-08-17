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
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public ClickTrackerService(ILogger<ClickTrackerService> logger, IServiceProvider serviceProvider,
        IConnection connection)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _connection = connection;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ClickTrackerService is starting.");
        
        await InitializeChannelAsync(stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Проверяем состояние соединения
                if (_channel == null || _channel.IsClosed)
                {
                    _logger.LogWarning("Channel is closed, attempting to reconnect...");
                    await InitializeChannelAsync(stoppingToken);
                }

                await Task.Delay(5000, stoppingToken); // Проверяем каждые 5 секунд
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in main loop");
                await Task.Delay(10000, stoppingToken); // Ждем 10 секунд перед повтором
            }
        }
    }

    private async Task InitializeChannelAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Закрываем старый канал если он существует
            if (_channel != null)
            {
                try
                {
                    await _channel.CloseAsync();
                    _channel.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing old channel");
                }
            }

            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await _channel.QueueDeclareAsync(
                queue: QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            // Настраиваем QoS - обрабатываем по одному сообщению за раз
            await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += ProcessMessage;

            await _channel.BasicConsumeAsync(
                queue: QueueName, 
                autoAck: false, 
                consumer: consumer, 
                cancellationToken: cancellationToken);

            _logger.LogInformation("Consumer started successfully. Waiting for messages on queue '{QueueName}'.", QueueName);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task ProcessMessage(object sender, BasicDeliverEventArgs ea)
    {
        var body = ea.Body.ToArray();
        var messageString = Encoding.UTF8.GetString(body);
        _logger.LogInformation("Processing message: {Message}", messageString);

        try
        {
            var message = JsonSerializer.Deserialize<ClickMessage>(messageString);
            if (message?.ShortenedUrlId == null || message.ShortenedUrlId == Guid.Empty)
            {
                _logger.LogWarning("Invalid message format: {Message}", messageString);
                await _channel!.BasicNackAsync(ea.DeliveryTag, false, false); // Не возвращаем в очередь
                return;
            }

            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var shortenedUrl = await dbContext.ShortenedUrls
                    .FirstOrDefaultAsync(u => u.Id == message.ShortenedUrlId);

                if (shortenedUrl != null)
                {
                    shortenedUrl.ClickCount++;
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("Updated click count for URL ID: {UrlId}. New count: {ClickCount}", 
                        message.ShortenedUrlId, shortenedUrl.ClickCount);
                }
                else
                {
                    _logger.LogWarning("URL with ID: {UrlId} not found in database", message.ShortenedUrlId);
                }
            }

            await _channel!.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize message: {Message}", messageString);
            await _channel!.BasicNackAsync(ea.DeliveryTag, false, false); // Не возвращаем в очередь невалидные сообщения
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message: {Message}", messageString);
            await _channel!.BasicNackAsync(ea.DeliveryTag, false, true); // Возвращаем в очередь для повтора
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ClickTrackerService is stopping...");
        
        try
        {
            if (_channel?.IsOpen == true)
            {
                await _channel.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing channel during shutdown");
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _semaphore.Dispose();
        _channel?.Dispose();
        base.Dispose();
    }

    private class ClickMessage
    {
        public Guid ShortenedUrlId { get; set; }
    }
}