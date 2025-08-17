using LinkPulse.Worker.Data;
using LinkPulse.Worker.Services;
using RabbitMQ.Client;
using Microsoft.EntityFrameworkCore;

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