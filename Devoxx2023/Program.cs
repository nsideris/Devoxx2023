using Devoxx2023;
using Serilog;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
builder.Services.Configure<ServiceBusOptions>(options =>
    options.ConnectionString = builder.Configuration["ServiceBus:ConnectionString"]);

builder.Services.AddControllers().AddJsonOptions(x =>
{
    x.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHostedService<WorkerServiceBus>();
var app = builder.Build();

ApplicationStatus? ServiceBusStatus = null;

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors(builder => builder
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader()
);

app.MapGet("/GetEnvironment", () => $"You are in Environment: {Environment.GetEnvironmentVariable("Role")}")
    .WithOpenApi();

app.MapGet("/ServiceBusCount", (
    IEnumerable<IHostedService> hostedServices) =>
{
    var serviceBus = (WorkerServiceBus) hostedServices.Single(x => x.GetType() == typeof(WorkerServiceBus));
    return $"{ServiceBusStatus} Count:{serviceBus.ReceivedCount}";
});

app.MapPost("/ApplicationState",
    async (ApplicationState? applicationState, CancellationToken ct, IEnumerable<IHostedService> hostedServices,
        ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("ApplicationState");

        logger.LogInformation($"Status is {applicationState.Status}");
        var serviceBus = (WorkerServiceBus) hostedServices.Single(x => x.GetType() == typeof(WorkerServiceBus));
        if (applicationState is {Status: ApplicationStatus.Inactive})
        {
            await serviceBus.StopAsync(ct);
        }
        else if (applicationState is {Status: ApplicationStatus.Active})
        {
            await serviceBus.StartAsync(ct);
        }

        ServiceBusStatus = applicationState.Status;
    }).WithOpenApi();


app.Run();


public class ApplicationState
{
    /// <summary>
    /// Status of the application.
    /// </summary>
    public ApplicationStatus Status { get; set; }
}

public enum ApplicationStatus
{
    Active,
    Inactive
}