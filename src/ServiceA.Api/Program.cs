using Microsoft.EntityFrameworkCore;
using ServiceA.Api.Configuration;
using ServiceA.Api.Data;
using ServiceA.Api.Messaging;
using ServiceA.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("ProductDb")
    ?? throw new InvalidOperationException("Connection string 'ProductDb' is not configured.");

builder.Services.AddDbContext<ProductDbContext>(options => options.UseMySQL(connectionString));

builder.Services.AddScoped<ProductService>();
builder.Services.AddSingleton<RabbitMqConnectionProvider>();
builder.Services.AddHostedService<OutboxPublisherService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
await DatabaseInitializer.InitializeAsync(app.Services, logger);

app.Run();
