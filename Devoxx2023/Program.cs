using Devoxx2023;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.TryAddSingleton<IWorkerServiceBus, WorkerServiceBus>();
var app = builder.Build();

ApplicationStatus ServiceBusStatus = ApplicationStatus.Inactive;

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
    [FromServices] IWorkerServiceBus serviceBusWorker) => TypedResults.Ok(new
{
    Status = ServiceBusStatus,
    Environment = Environment.GetEnvironmentVariable("Role"),
    QueueMessagesProccessed = serviceBusWorker.GetReceivedMessages()
}));

app.MapPost("ResetCount", ([FromServices] IWorkerServiceBus serviceBusWorker) =>
{
    serviceBusWorker.ResetCount();
    return "Count was reset";
});

app.MapPost("/ApplicationState",
    async (ApplicationState? applicationState, CancellationToken ct, [FromServices] IWorkerServiceBus serviceBusWorker,
        ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("ApplicationState");

        logger.LogInformation($"Status is {applicationState.Status}");
        if (applicationState is {Status: ApplicationStatus.Inactive})
        {
            await serviceBusWorker.StopAsync(ct);
        }
        else if (applicationState is {Status: ApplicationStatus.Active})
        {
            await serviceBusWorker.StartAsync(ct);
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