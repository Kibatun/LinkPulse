using LinkPulse.Core.Data;
using LinkPulse.Worker.Services;
using RabbitMQ.Client;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Polly;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        IConfiguration configuration = hostContext.Configuration;

        var dbConnectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(dbConnectionString))
            throw new Exception("DefaultConnection is not configured");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(dbConnectionString));

        var rabbitMqConnectionString = configuration.GetConnectionString("RabbitMqConnection");
        if (string.IsNullOrEmpty(rabbitMqConnectionString))
            throw new Exception("RabbitMqConnection is not configured");

        services.AddSingleton<IConnectionFactory>(sp => new ConnectionFactory
        {
            Uri = new Uri(rabbitMqConnectionString),
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            TopologyRecoveryEnabled = true
        });

        services.AddSingleton<IConnection>(sp =>
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

        var retryPolicy = Policy
            .Handle<NpgsqlException>() // Ошибки подключения Postgres
            .Or<DbUpdateException>() // Ошибки сохранения EF Core
            .WaitAndRetryAsync(
                3, // Попробовать 3 раза
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Задержка: 2, 4, 8 секунд
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    var logger = services.BuildServiceProvider().GetService<ILogger<Program>>();
                    logger?.LogWarning(exception,
                        "[Polly] Retry {RetryCount} due to {ExceptionType}. Waiting {TimeSpan}s before next try.",
                        retryCount, exception.GetType().Name, timeSpan.TotalSeconds);
                });

        services.AddSingleton<IAsyncPolicy>(retryPolicy);

        services.AddHostedService<ClickTrackerService>();
    })
    .Build();

// Graceful shutdown для RabbitMQ
host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() =>
{
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    try
    {
        var connection = host.Services.GetService<IConnection>();
        if (connection != null && connection.IsOpen)
        {
            connection.CloseAsync().Wait(TimeSpan.FromSeconds(5));
            connection.Dispose();
            logger.LogInformation("RabbitMQ connection closed gracefully");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during graceful shutdown");
    }
});

host.Run();