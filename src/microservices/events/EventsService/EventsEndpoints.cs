namespace EventsService;

public static class EventsEndpoints
{
    public static void MapEventsEndpoints(this WebApplication app)
    {
        // Health check
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
            .WithName("HealthCheck")
            .WithTags("Health");

        // Health check for tests at /api/events/health
        app.MapGet("/api/events/health", () => Results.Ok(new { status = true }))
            .WithName("EventsHealthCheck")
            .WithTags("Health");

        // POST /api/events/user - создание события пользователя
        app.MapPost("/api/events/user", async (UserEvent userEvent, IKafkaProducerService producer) =>
        {
            await producer.ProduceAsync("user-events", userEvent);
            return Results.Created($"/api/events/user/{userEvent.UserId}", new { status = "success" });
        })
            .WithName("CreateUserEvent")
            .WithTags("Events");

        // POST /api/events/payment - создание события платежа
        app.MapPost("/api/events/payment", async (PaymentEvent paymentEvent, IKafkaProducerService producer) =>
        {
            await producer.ProduceAsync("payment-events", paymentEvent);
            return Results.Created($"/api/events/payment/{paymentEvent.PaymentId}", new { status = "success" });
        })
            .WithName("CreatePaymentEvent")
            .WithTags("Events");

        // POST /api/events/movie - создание события фильма
        app.MapPost("/api/events/movie", async (MovieEvent movieEvent, IKafkaProducerService producer) =>
        {
            await producer.ProduceAsync("movie-events", movieEvent);
            return Results.Created($"/api/events/movie/{movieEvent.MovieId}", new { status = "success" });
        })
            .WithName("CreateMovieEvent")
            .WithTags("Events");
    }
}
