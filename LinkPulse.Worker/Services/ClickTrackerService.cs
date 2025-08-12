using Microsoft.EntityFrameworkCore.Metadata;
using RabbitMQ.Client;

namespace LinkPulse.Worker.Services;

public class ClickTrackerService : BackgroundService
{
    private readonly ILogger<ClickTrackerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnection _connection;
    private IModel? _channel;
    private const string QueueName = "click_tracking_queue";

    public ClickTrackerService(ILogger<ClickTrackerService> logger, IServiceProvider serviceProvider, IConnection connection)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _connection = connection;
    }
    
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _channel = _connection.CreateModel();
        _channel.QueueDeclare(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
}