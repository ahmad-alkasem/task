using Microsoft.EntityFrameworkCore;
using ServiceB.Api.Configuration;
using ServiceB.Api.Data;
using ServiceB.Api.Messaging;
using ServiceB.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<ConsumerOptions>(builder.Configuration.GetSection(ConsumerOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("SyncDb")
    ?? throw new InvalidOperationException("Connection string 'SyncDb' is not configured.");

builder.Services.AddDbContext<SyncDbContext>(options => options.UseMySQL(connectionString));

builder.Services.AddScoped<SyncProcessor>();
builder.Services.AddScoped<SyncStatusWriter>();
builder.Services.AddSingleton<RabbitMqConnectionProvider>();
builder.Services.AddHostedService<SyncConsumerService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
await DatabaseInitializer.InitializeAsync(app.Services, logger);

app.Run();
