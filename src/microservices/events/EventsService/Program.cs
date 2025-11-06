using EventsService;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Kafka configuration
var kafkaBrokers = builder.Configuration.GetValue<string>("KAFKA_BROKERS") ?? "localhost:9092";

// Register Kafka Producer
builder.Services.AddSingleton<IKafkaProducerService>(sp =>
    new KafkaProducerService(kafkaBrokers, sp.GetRequiredService<ILogger<KafkaProducerService>>()));

// Register Kafka Consumer
builder.Services.AddHostedService(sp =>
    new KafkaConsumerService(kafkaBrokers, sp.GetRequiredService<ILogger<KafkaConsumerService>>()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Register Events endpoints
app.MapEventsEndpoints();

app.Run();
