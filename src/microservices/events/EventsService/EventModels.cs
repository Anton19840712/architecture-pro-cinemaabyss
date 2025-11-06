namespace EventsService;

public record UserEvent(string UserId, string Action, DateTime Timestamp);
public record PaymentEvent(string PaymentId, decimal Amount, string Status);
public record MovieEvent(string MovieId, string Title, string Action);
