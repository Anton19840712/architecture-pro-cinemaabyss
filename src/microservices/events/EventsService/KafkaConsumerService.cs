using Confluent.Kafka;

namespace EventsService;

public class KafkaConsumerService : BackgroundService
{
    private readonly string _bootstrapServers;
    private readonly ILogger<KafkaConsumerService> _logger;

    public KafkaConsumerService(string bootstrapServers, ILogger<KafkaConsumerService> logger)
    {
        _bootstrapServers = bootstrapServers;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Передаем управление, чтобы не блокировать StartAsync
        await Task.Yield();

        _logger.LogInformation("Kafka Consumer Service запускается...");

        var topics = new[] { "user-events", "payment-events", "movie-events" };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = new ConsumerConfig
                {
                    BootstrapServers = _bootstrapServers,
                    GroupId = "events-service-consumer-group",
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    EnableAutoCommit = true,
                    EnableAutoOffsetStore = true
                };

                using var consumer = new ConsumerBuilder<Ignore, string>(config)
                    .SetLogHandler((_, logMessage) => {
                        if (logMessage.Level <= SyslogLevel.Warning)
                        {
                            _logger.LogWarning($"Kafka: {logMessage.Message}");
                        }
                    })
                    .Build();

                consumer.Subscribe(topics);
                _logger.LogInformation("Consumer подписан на топики: user-events, payment-events, movie-events");

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = consumer.Consume(TimeSpan.FromSeconds(1));

                        if (consumeResult != null)
                        {
                            _logger.LogInformation(
                                $"📥 Получено событие из '{consumeResult.Topic}': {consumeResult.Message.Value} " +
                                $"[Partition: {consumeResult.Partition}, Offset: {consumeResult.Offset}]");
                        }
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogWarning(ex, "Ошибка при чтении из Kafka, повторяем...");
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }

                consumer.Close();
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kafka недоступна, повторная попытка через 10 секунд...");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("Consumer остановлен");
    }
}
