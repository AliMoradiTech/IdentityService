using System.Text.Json;
using Confluent.Kafka;
using idnetityServiceWedApi.Data;
using Microsoft.EntityFrameworkCore;

namespace idnetityServiceWedApi.Messaging;

public sealed class OutboxDispatcher(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var producer = new ProducerBuilder<Null, string>(new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"], Acks = Acks.All, EnableIdempotence = true
        }).Build();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                var messages = await db.OutboxMessages.Where(x => x.DispatchedAt == null).OrderBy(x => x.CreatedAt).Take(20).ToListAsync(stoppingToken);
                foreach (var message in messages)
                {
                    try
                    {
                        var headers = new Headers();
                        if (message.TraceParent is not null) headers.Add("traceparent", System.Text.Encoding.UTF8.GetBytes(message.TraceParent));
                        if (message.TraceState is not null) headers.Add("tracestate", System.Text.Encoding.UTF8.GetBytes(message.TraceState));
                        await producer.ProduceAsync(configuration["Kafka:Topics:UserRegisteredEvents"]!, new Message<Null, string> { Value = message.Payload, Headers = headers }, stoppingToken);
                        message.DispatchedAt = DateTimeOffset.UtcNow; message.Attempts++; message.Error = null;
                    }
                    catch (Exception ex) { message.Attempts++; message.Error = ex.Message; logger.LogError(ex, "Outbox dispatch failed for {EventId}", message.EventId); }
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Outbox polling failed"); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
