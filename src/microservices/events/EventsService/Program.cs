// ==============================================================================
// EVENTS SERVICE - работа с Kafka (Producer + Consumer)
// ==============================================================================

using Confluent.Kafka;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// 1. КОНФИГУРАЦИЯ
var kafkaBrokers = Environment.GetEnvironmentVariable("KAFKA_BROKERS") ?? "localhost:9092";

Console.WriteLine($"=== Events Service Configuration ===");
Console.WriteLine($"Kafka Brokers: {kafkaBrokers}");
Console.WriteLine($"===================================");

// 2. РЕГИСТРАЦИЯ СЕРВИСОВ
builder.Services.AddSingleton<IKafkaProducerService>(sp =>
    new KafkaProducerService(kafkaBrokers, sp.GetRequiredService<ILogger<KafkaProducerService>>()));

// Регистрируем Consumer как Hosted Service (фоновый сервис)
builder.Services.AddHostedService<KafkaConsumerService>(sp =>
    new KafkaConsumerService(kafkaBrokers, sp.GetRequiredService<ILogger<KafkaConsumerService>>()));

var app = builder.Build();

// 3. API ENDPOINTS

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// POST /api/events/user - создание события пользователя
app.MapPost("/api/events/user", async (UserEvent userEvent, IKafkaProducerService producer) =>
{
    await producer.ProduceAsync("user-events", userEvent);
    return Results.Created($"/api/events/user/{userEvent.UserId}", userEvent);
});

// POST /api/events/payment - создание события платежа
app.MapPost("/api/events/payment", async (PaymentEvent paymentEvent, IKafkaProducerService producer) =>
{
    await producer.ProduceAsync("payment-events", paymentEvent);
    return Results.Created($"/api/events/payment/{paymentEvent.PaymentId}", paymentEvent);
});

// POST /api/events/movie - создание события фильма
app.MapPost("/api/events/movie", async (MovieEvent movieEvent, IKafkaProducerService producer) =>
{
    await producer.ProduceAsync("movie-events", movieEvent);
    return Results.Created($"/api/events/movie/{movieEvent.MovieId}", movieEvent);
});

// 4. ЗАПУСК
var port = Environment.GetEnvironmentVariable("PORT") ?? "8082";
Console.WriteLine($"Starting Events Service on port {port}");
app.Run($"http://0.0.0.0:{port}");

// ==============================================================================
// МОДЕЛИ СОБЫТИЙ
// ==============================================================================

public record UserEvent(string UserId, string Action, DateTime Timestamp);
public record PaymentEvent(string PaymentId, decimal Amount, string Status);
public record MovieEvent(string MovieId, string Title, string Action);

// ==============================================================================
// KAFKA PRODUCER SERVICE
// ==============================================================================

public interface IKafkaProducerService
{
    Task ProduceAsync<T>(string topic, T message);
}

public class KafkaProducerService : IKafkaProducerService, IDisposable
{
    private readonly IProducer<Null, string> _producer;
    private readonly ILogger<KafkaProducerService> _logger;

    public KafkaProducerService(string bootstrapServers, ILogger<KafkaProducerService> logger)
    {
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            // Настройки надёжности
            Acks = Acks.All, // Ждём подтверждения от всех реплик
            EnableIdempotence = true, // Идемпотентность (без дубликатов)
            MaxInFlight = 5,
            MessageSendMaxRetries = 3
        };

        _producer = new ProducerBuilder<Null, string>(config).Build();
        _logger.LogInformation($"Kafka Producer подключён к {bootstrapServers}");
    }

    public async Task ProduceAsync<T>(string topic, T message)
    {
        try
        {
            // Сериализуем объект в JSON
            var json = JsonSerializer.Serialize(message);

            // Отправляем в Kafka
            var deliveryResult = await _producer.ProduceAsync(topic, new Message<Null, string>
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

// ==============================================================================
// KAFKA CONSUMER SERVICE (фоновый сервис)
// ==============================================================================

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
        _logger.LogInformation("Kafka Consumer Service запускается...");

        var topics = new[] { "user-events", "payment-events", "movie-events" };

        // Retry loop - ждём пока Kafka станет доступна
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

                using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();

                // ПОДПИСЫВАЕМСЯ на все топики событий
                consumer.Subscribe(topics);

                _logger.LogInformation("Consumer подписан на топики: user-events, payment-events, movie-events");

                // Основной цикл потребления
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
                break; // Выходим из retry loop при нормальном завершении
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
