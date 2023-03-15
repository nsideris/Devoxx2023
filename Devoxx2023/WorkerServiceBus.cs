using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Devoxx2023;

public interface IWorkerServiceBus
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    int GetReceivedMessages();
}

public class WorkerServiceBus : IWorkerServiceBus, IAsyncDisposable
{
    private readonly string _connectionString;

    public WorkerServiceBus(IOptions<ServiceBusOptions> serviceBusOptions)
    {
        _connectionString = serviceBusOptions.Value.ConnectionString;
    }

    private int ReceivedCount = 0;

    // the client that owns the connection and can be used to create senders and receivers
    private ServiceBusClient _client = null!;

    // the processor that reads and processes messages from the queue
    private ServiceBusProcessor _processor = null!;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var clientOptions = new ServiceBusClientOptions()
        {
            TransportType = ServiceBusTransportType.AmqpWebSockets
        };

        _client = new ServiceBusClient(
            _connectionString,
            clientOptions);

        _processor = _client.CreateProcessor("devox2023", new ServiceBusProcessorOptions());

        _processor.ProcessMessageAsync += MessageHandler;

        _processor.ProcessErrorAsync += ErrorHandler;

        await _processor.StartProcessingAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _processor.StopProcessingAsync(cancellationToken);
        }
        finally
        {
            await _processor.DisposeAsync();
            await _client.DisposeAsync();
        }
    }

    public int GetReceivedMessages()
    {
        return ReceivedCount;
    }

    async Task MessageHandler(ProcessMessageEventArgs args)
    {
        string body = args.Message.Body.ToString();
        Console.WriteLine($"Received: {body}");
        Interlocked.Increment(ref ReceivedCount);
        await args.CompleteMessageAsync(args.Message);
    }

    Task ErrorHandler(ProcessErrorEventArgs args)
    {
        Console.WriteLine(args.Exception.ToString());
        return Task.CompletedTask;
    }


    public async ValueTask DisposeAsync()
    {
        await _processor.DisposeAsync();
        await _client.DisposeAsync();
    }
}

public class ServiceBusOptions
{
    public string ServiceBus = "ServiceBus";
    public string ConnectionString { get; set; }
}