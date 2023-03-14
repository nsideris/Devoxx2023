var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();


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

app.MapPost("/ApplicationState", (ApplicationState? applicationState) =>
{
    if (applicationState is {Status: ApplicationStatus.Inactive})
    {
        //UnRegister any background jobs/Subscriptions
    }
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