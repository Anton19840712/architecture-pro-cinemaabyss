using Confluent.Kafka;
using System.Text.Json;

namespace EventsService;

public interface IKafkaProducerService
{
    Task ProduceAsync<T>(string topic, T message);
}

public class KafkaProducerService : IKafkaProducerService, IDisposable
{
    private IProducer<Null, string>? _producer;
    private readonly ILogger<KafkaProducerService> _logger;
    private readonly string _bootstrapServers;
    private readonly object _lock = new object();

    public KafkaProducerService(string bootstrapServers, ILogger<KafkaProducerService> logger)
    {
        _logger = logger;
        _bootstrapServers = bootstrapServers;
    }

    private IProducer<Null, string> GetProducer()
    {
        if (_producer == null)
        {
            lock (_lock)
            {
                if (_producer == null)
                {
                    var config = new ProducerConfig
                    {
                        BootstrapServers = _bootstrapServers,
                        Acks = Acks.All,
                        EnableIdempotence = true,
                        MaxInFlight = 5,
                        MessageSendMaxRetries = 3
                    };

                    _producer = new ProducerBuilder<Null, string>(config)
                        .SetLogHandler((_, logMessage) => {
                            if (logMessage.Level <= SyslogLevel.Warning)
                            {
                                _logger.LogWarning($"Kafka: {logMessage.Message}");
                            }
                        })
                        .Build();
                    _logger.LogInformation($"Kafka Producer подключён к {_bootstrapServers}");
                }
            }
        }
        return _producer;
    }

    public async Task ProduceAsync<T>(string topic, T message)
    {
        try
        {
            var json = JsonSerializer.Serialize(message);
            var producer = GetProducer();
            var deliveryResult = await producer.ProduceAsync(topic, new Message<Null, string>
            {
                Value = json
            });

            _logger.LogInformation(
                $"✓ Событие отправлено в топик '{topic}': {json} " +
                $"[Partition: {deliveryResult.Partition}, Offset: {deliveryResult.Offset}]");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Ошибка отправки события в топик '{topic}'");
            throw;
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}
