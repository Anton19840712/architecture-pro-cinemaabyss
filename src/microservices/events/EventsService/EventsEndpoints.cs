namespace EventsService;

public static class EventsEndpoints
{
    public static void MapEventsEndpoints(this WebApplication app)
    {
        // Health check
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
            .WithName("HealthCheck")
            .WithTags("Health");

        // POST /api/events/user - создание события пользователя
        app.MapPost("/api/events/user", async (UserEvent userEvent, IKafkaProducerService producer) =>
        {
            await producer.ProduceAsync("user-events", userEvent);
            return Results.Created($"/api/events/user/{userEvent.UserId}", userEvent);
        })
            .WithName("CreateUserEvent")
            .WithTags("Events");

        // POST /api/events/payment - создание события платежа
        app.MapPost("/api/events/payment", async (PaymentEvent paymentEvent, IKafkaProducerService producer) =>
        {
            await producer.ProduceAsync("payment-events", paymentEvent);
            return Results.Created($"/api/events/payment/{paymentEvent.PaymentId}", paymentEvent);
        })
            .WithName("CreatePaymentEvent")
            .WithTags("Events");

        // POST /api/events/movie - создание события фильма
        app.MapPost("/api/events/movie", async (MovieEvent movieEvent, IKafkaProducerService producer) =>
        {
            await producer.ProduceAsync("movie-events", movieEvent);
            return Results.Created($"/api/events/movie/{movieEvent.MovieId}", movieEvent);
        })
            .WithName("CreateMovieEvent")
            .WithTags("Events");
    }
}
