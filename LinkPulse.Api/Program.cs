using LinkPulse.Api.Services;
using LinkPulse.Core.Data;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
    throw new Exception("DefaultConnection connection string is not configured");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

var rabbitMqConnectionString = builder.Configuration.GetConnectionString("RabbitMqConnection");
if (string.IsNullOrEmpty(rabbitMqConnectionString))
    throw new Exception("RabbitMqConnection is not configured");

builder.Services.AddSingleton<IConnectionFactory>(sp =>
{
    return new ConnectionFactory()
    {
        Uri = new Uri(rabbitMqConnectionString),
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
        TopologyRecoveryEnabled = true,
        RequestedHeartbeat = TimeSpan.FromSeconds(60)
    };
});

builder.Services.AddSingleton<IConnection>(sp =>
{
    var factory = sp.GetRequiredService<IConnectionFactory>();
    var logger = sp.GetRequiredService<ILogger<Program>>();

    try
    {
        var connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        logger.LogInformation("Successfully connected to RabbitMQ");
        return connection;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to connect to RabbitMQ");
        throw;
    }
});

builder.Services.AddSingleton<RabbitMqPublisher>(sp =>
{
    var connection = sp.GetRequiredService<IConnection>();
    var logger = sp.GetRequiredService<ILogger<RabbitMqPublisher>>();
    return RabbitMqPublisher.CreateAsync(connection, logger).GetAwaiter().GetResult();
});

var app = builder.Build();

// Проверка подключений при запуске
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        // Проверяем подключение к БД
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.CanConnectAsync();
        logger.LogInformation("Successfully connected to database");

        // Проверяем подключение к RabbitMQ
        var connection = scope.ServiceProvider.GetRequiredService<IConnection>();
        if (connection.IsOpen)
        {
            logger.LogInformation("RabbitMQ connection is open and ready");
        }
        else
        {
            logger.LogWarning("RabbitMQ connection is not open");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to verify connections during startup");
        throw;
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Graceful shutdown для RabbitMQ
app.Lifetime.ApplicationStopping.Register(async () =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    try
    {
        // Сначала останавливаем publisher
        var publisher = app.Services.GetService<RabbitMqPublisher>();
        if (publisher != null)
        {
            await publisher.DisposeAsync();
            logger.LogInformation("RabbitMQ publisher disposed");
        }

        // Затем закрываем соединение
        var connection = app.Services.GetService<IConnection>();
        if (connection != null && connection.IsOpen)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                await connection.CloseAsync(cts.Token);
            }
            connection.Dispose();
            logger.LogInformation("RabbitMQ connection closed gracefully");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during graceful shutdown");
    }
});

app.MapControllers();
app.Run();