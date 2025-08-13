using LinkPulse.Api.Data;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

var rabbitMqConnectionString = builder.Configuration.GetConnectionString("RabbitMqConnection");
if (string.IsNullOrEmpty(rabbitMqConnectionString))
    throw new Exception("RabbitMqConnection is empty");

builder.Services.AddSingleton<IConnection>(sp =>
{
    var factory = new ConnectionFactory()
    {
        Uri = new Uri(rabbitMqConnectionString)
    };
    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

var app = builder.Build();

//Автоматическая миграция при каждом запуске
// using (var scope = app.Services.CreateScope())
// {
//     var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
//     dbContext.Database.Migrate();
// }
//


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.Lifetime.ApplicationStopping.Register(() =>
{
    var connection = app.Services.GetService<IConnection>();
    if (connection != null && connection.IsOpen)
    {
        connection.CloseAsync();
        connection.Dispose();
    }
});

app.MapControllers();
app.Run();