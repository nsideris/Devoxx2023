using System.Collections;
using System.Runtime;
using Devoxx2023;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
builder.Services.Configure<ServiceBusOptions>(options =>
    options.ConnectionString = builder.Configuration["ServiceBus:ConnectionString"]);


builder.Services.AddHostedService<WorkerServiceBus>();
var app = builder.Build();

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
    return $"{serviceBus.ReceivedCount}";
});

app.MapPost("/ApplicationState",
    async (ApplicationState? applicationState, CancellationToken ct, IEnumerable<IHostedService> hostedServices) =>
    {
        var serviceBus = (WorkerServiceBus) hostedServices.Single(x => x.GetType() == typeof(WorkerServiceBus));
        if (applicationState is {Status: ApplicationStatus.Inactive})
        {
            await serviceBus.StopAsync(ct);
        }
        //else if (applicationState is {Status: ApplicationStatus.Active})
        //{
        //    await serviceBus.StartAsync(ct);
        //}
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