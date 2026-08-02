using System.Text;
using Confluent.Kafka;
using Dapper;
using idnetityServiceWedApi.Data;
using Microsoft.Data.SqlClient;

namespace idnetityServiceWedApi.Messaging;

public sealed class OutboxDispatcher(IConfiguration configuration, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private readonly string _connectionString = configuration.GetConnectionString("AuthDb")
        ?? throw new InvalidOperationException("ConnectionStrings:AuthDb is required.");

    /// <summary>How long a claimed batch stays reserved before another dispatcher may retry it.</summary>
    private static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(5);

    // Atomically claims a batch so concurrent replicas never publish the same row.
    // READPAST skips rows another dispatcher is already claiming instead of blocking on them.
    // The lock is time-bounded so a crashed dispatcher's rows become claimable again.
    private const string ClaimPendingSql = """
        WITH candidates AS
        (
            SELECT TOP (@batchSize) *
            FROM IAM.OutboxMessage WITH (READPAST, UPDLOCK, ROWLOCK)
            WHERE DispatchedAt IS NULL
              AND DeadLetteredAt IS NULL
              AND (LockedUntil IS NULL OR LockedUntil < @now)
            ORDER BY CreatedAt, Id
        )
        UPDATE candidates
        SET LockId = @lockId, LockedUntil = @lockedUntil
        OUTPUT inserted.Id, inserted.EventId, inserted.Type, inserted.Payload, inserted.TraceParent,
               inserted.TraceState, inserted.OccurredAt, inserted.CreatedAt, inserted.DispatchedAt,
               inserted.Attempts, inserted.Error, inserted.DeadLetteredAt, inserted.LockId, inserted.LockedUntil;
        """;

    private const string MarkDispatchedSql = """
        UPDATE IAM.OutboxMessage
        SET DispatchedAt = @dispatchedAt, Attempts = @attempts, Error = NULL, LockId = NULL, LockedUntil = NULL
        WHERE Id = @id AND LockId = @lockId
        """;

    private const string MarkFailedSql = """
        UPDATE IAM.OutboxMessage
        SET Attempts = @attempts, Error = @error, DeadLetteredAt = @deadLetteredAt, LockId = NULL, LockedUntil = NULL
        WHERE Id = @id AND LockId = @lockId
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int batchSize = configuration.GetValue<int?>("Outbox:BatchSize") ?? 20;
        int maxAttempts = configuration.GetValue<int?>("Outbox:MaxAttempts") ?? 10;
        var pollingInterval = TimeSpan.FromSeconds(configuration.GetValue<int?>("Outbox:PollingIntervalSeconds") ?? 2);
        string topic = configuration["Kafka:Topics:UserRegisteredEvents"]
            ?? throw new InvalidOperationException("Kafka:Topics:UserRegisteredEvents is required.");

        using var producer = new ProducerBuilder<Null, string>(new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"], Acks = Acks.All, EnableIdempotence = true
        }).Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(stoppingToken);

                DateTimeOffset now = DateTimeOffset.UtcNow;
                Guid lockId = Guid.NewGuid();
                IEnumerable<OutboxMessage> messages = await connection.QueryAsync<OutboxMessage>(
                    new CommandDefinition(ClaimPendingSql,
                        new { batchSize, now, lockId, lockedUntil = now.Add(ClaimDuration) }, cancellationToken: stoppingToken));

                foreach (OutboxMessage message in messages)
                {
                    int attempts = message.Attempts + 1;
                    try
                    {
                        var headers = new Headers();
                        if (message.TraceParent is not null) headers.Add("traceparent", Encoding.UTF8.GetBytes(message.TraceParent));
                        if (message.TraceState is not null) headers.Add("tracestate", Encoding.UTF8.GetBytes(message.TraceState));

                        await producer.ProduceAsync(topic, new Message<Null, string> { Value = message.Payload, Headers = headers }, stoppingToken);

                        int affected = await connection.ExecuteAsync(new CommandDefinition(MarkDispatchedSql,
                            new { dispatchedAt = DateTimeOffset.UtcNow, attempts, id = message.Id, lockId = message.LockId }, cancellationToken: stoppingToken));
                        if (affected != 1)
                        {
                            logger.LogWarning("Outbox message {EventId} was published after its claim ownership expired; a duplicate delivery is possible.", message.EventId);
                        }
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Give up after MaxAttempts so a permanently-broken message stops consuming the poll loop forever.
                        bool exhausted = attempts >= maxAttempts;
                        int affected = await connection.ExecuteAsync(new CommandDefinition(MarkFailedSql,
                            new
                            {
                                attempts,
                                error = Truncate(ex.Message, 2048),
                                deadLetteredAt = exhausted ? DateTimeOffset.UtcNow : (DateTimeOffset?)null,
                                id = message.Id,
                                lockId = message.LockId
                            }, cancellationToken: stoppingToken));

                        if (affected != 1)
                        {
                            logger.LogWarning("Could not record failure for outbox event {EventId} because claim ownership was lost.", message.EventId);
                            continue;
                        }

                        if (exhausted)
                        {
                            logger.LogError(ex, "Outbox message {EventId} dead-lettered after {Attempts} failed dispatch attempts.", message.EventId, attempts);
                        }
                        else
                        {
                            logger.LogError(ex, "Outbox dispatch failed for {EventId} (attempt {Attempts}/{MaxAttempts}).", message.EventId, attempts, maxAttempts);
                        }
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Outbox polling failed");
            }

            await Task.Delay(pollingInterval, stoppingToken);
        }
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
