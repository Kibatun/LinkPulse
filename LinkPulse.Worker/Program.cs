using LinkPulse.Worker.Data;
using RabbitMQ.Client;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        IConfiguration configuration = hostContext.Configuration;

        var dbConnectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(dbConnectionString));

        var rabbitMqConnectionString = configuration.GetConnectionString("RabbitMqConnection");
        if (string.IsNullOrEmpty(rabbitMqConnectionString))
            throw new Exception("RabbitMqConnection is empty");

        services.AddSingleton<IConnectionFactory>(sp => new ConnectionFactory
        {
            Uri = new Uri(rabbitMqConnectionString),
        });

        services.AddSingleton<IConnection>(sp =>
        {
            var factory = sp.GetRequiredService<IConnectionFactory>();
            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });

        services.AddHostedService<ClickTrackerService>();
    })
    .Build();

host.Run();