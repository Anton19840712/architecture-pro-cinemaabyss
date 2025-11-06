// ==============================================================================
// ПРОКСИ-СЕРВИС (API Gateway) с паттерном Strangler Fig
// ==============================================================================

var builder = WebApplication.CreateBuilder(args);

// 1. РЕГИСТРАЦИЯ СЕРВИСОВ
// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// HttpClient для отправки запросов в другие сервисы
builder.Services.AddHttpClient();

// 2. КОНФИГУРАЦИЯ ПРОКСИ
// Читаем переменные окружения и создаём конфигурацию
builder.Services.AddSingleton<ProxyConfiguration>(sp =>
{
    var config = new ProxyConfiguration
    {
        // URL монолита (старая система)
        MonolithUrl = Environment.GetEnvironmentVariable("MONOLITH_URL") ?? "http://localhost:8080",

        // URL нового микросервиса movies
        MoviesServiceUrl = Environment.GetEnvironmentVariable("MOVIES_SERVICE_URL") ?? "http://localhost:8081",

        // URL сервиса событий (events)
        EventsServiceUrl = Environment.GetEnvironmentVariable("EVENTS_SERVICE_URL") ?? "http://localhost:8082",

        // Включена ли постепенная миграция
        GradualMigration = bool.Parse(Environment.GetEnvironmentVariable("GRADUAL_MIGRATION") ?? "true"),

        // КЛЮЧЕВОЙ ПАРАМЕТР: процент трафика на новый сервис (0-100)
        MoviesMigrationPercent = int.Parse(Environment.GetEnvironmentVariable("MOVIES_MIGRATION_PERCENT") ?? "50")
    };

    // Логируем конфигурацию при старте
    Console.WriteLine($"=== Proxy Configuration ===");
    Console.WriteLine($"Monolith URL: {config.MonolithUrl}");
    Console.WriteLine($"Movies Service URL: {config.MoviesServiceUrl}");
    Console.WriteLine($"Events Service URL: {config.EventsServiceUrl}");
    Console.WriteLine($"Gradual Migration: {config.GradualMigration}");
    Console.WriteLine($"Movies Migration Percent: {config.MoviesMigrationPercent}%");
    Console.WriteLine($"===========================");

    return config;
});

var app = builder.Build();

// 2.5 SWAGGER UI (в Development)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 3. HEALTH CHECK ENDPOINT
// Простая проверка здоровья сервиса
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// 4. ПОДКЛЮЧАЕМ MIDDLEWARE ДЛЯ ПРОКСИРОВАНИЯ
// Все запросы будут проходить через ProxyMiddleware
app.UseMiddleware<ProxyMiddleware>();

// 5. ЗАПУСК СЕРВЕРА
var port = Environment.GetEnvironmentVariable("PORT") ?? "8000";
Console.WriteLine($"Starting Proxy Service on port {port}");
app.Run($"http://0.0.0.0:{port}");

// ==============================================================================
// КЛАССЫ
// ==============================================================================

/// <summary>
/// Конфигурация прокси-сервиса
/// </summary>
public class ProxyConfiguration
{
    public string MonolithUrl { get; set; } = "";
    public string MoviesServiceUrl { get; set; } = "";
    public string EventsServiceUrl { get; set; } = "";
    public bool GradualMigration { get; set; }
    public int MoviesMigrationPercent { get; set; }
}

/// <summary>
/// Middleware для проксирования запросов
/// НЮАНС: Middleware - это компонент в pipeline ASP.NET Core,
/// который обрабатывает каждый HTTP запрос
/// </summary>
public class ProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProxyConfiguration _config;
    private readonly Random _random;
    private readonly ILogger<ProxyMiddleware> _logger;

    public ProxyMiddleware(
        RequestDelegate next,
        IHttpClientFactory httpClientFactory,
        ProxyConfiguration config,
        ILogger<ProxyMiddleware> logger)
    {
        _next = next;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _random = new Random(); // Для случайного выбора
        _logger = logger;
    }

    /// <summary>
    /// Главный метод middleware - вызывается для каждого запроса
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.ToString();

        // Пропускаем health check и swagger (не проксируем)
        if (path == "/health" || path.StartsWith("/swagger"))
        {
            await _next(context);
            return;
        }

        // НЮАНС 1: Определяем, куда направить запрос
        string targetUrl = DetermineTargetUrl(path);

        _logger.LogInformation($"Proxying: {context.Request.Method} {path} -> {targetUrl}");

        try
        {
            // НЮАНС 2: Проксируем запрос
            await ProxyRequest(context, targetUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Proxy error to {targetUrl}");
            context.Response.StatusCode = 502; // Bad Gateway
            await context.Response.WriteAsJsonAsync(new { error = "Proxy error", message = ex.Message });
        }
    }

    /// <summary>
    /// НЮАНС 3: ЛОГИКА STRANGLER FIG PATTERN
    /// Определяет, в какой сервис направить запрос
    /// </summary>
    private string DetermineTargetUrl(string path)
    {
        // События -> events-service
        if (path.StartsWith("/api/events"))
        {
            return _config.EventsServiceUrl;
        }

        // Фильмы -> ЗДЕСЬ МАГИЯ ПОСТЕПЕННОЙ МИГРАЦИИ
        if (path.StartsWith("/api/movies"))
        {
            if (_config.GradualMigration)
            {
                // КЛЮЧЕВАЯ ЛОГИКА:
                // Генерируем случайное число от 0 до 99
                int randomValue = _random.Next(100);

                // Если число < процента миграции -> новый сервис
                // Иначе -> старый монолит
                //
                // Пример: MOVIES_MIGRATION_PERCENT = 30
                // - randomValue = 15 (< 30) -> movies-service ✓
                // - randomValue = 45 (>= 30) -> monolith ✓
                // Итого: примерно 30% запросов идут в новый сервис

                if (randomValue < _config.MoviesMigrationPercent)
                {
                    _logger.LogInformation($"→ Movies Service (random: {randomValue} < {_config.MoviesMigrationPercent}%)");
                    return _config.MoviesServiceUrl;
                }
                else
                {
                    _logger.LogInformation($"→ Monolith (random: {randomValue} >= {_config.MoviesMigrationPercent}%)");
                    return _config.MonolithUrl;
                }
            }
            else
            {
                // Полная миграция (100% в новый сервис)
                return _config.MoviesServiceUrl;
            }
        }

        // Все остальное -> монолит
        return _config.MonolithUrl;
    }

    /// <summary>
    /// НЮАНС 4: ПРОКСИРОВАНИЕ ЗАПРОСА
    /// Копируем запрос от клиента и отправляем в целевой сервис
    /// </summary>
    private async Task ProxyRequest(HttpContext context, string targetBaseUrl)
    {
        var client = _httpClientFactory.CreateClient();

        // Формируем полный URL: базовый URL + путь + query параметры
        var targetUrl = $"{targetBaseUrl}{context.Request.Path}{context.Request.QueryString}";

        // Создаём HTTP запрос
        var requestMessage = new HttpRequestMessage
        {
            Method = new HttpMethod(context.Request.Method),
            RequestUri = new Uri(targetUrl)
        };

        // НЮАНС 5: КОПИРУЕМ ЗАГОЛОВКИ
        // Важно сохранить все заголовки, кроме Host
        foreach (var header in context.Request.Headers)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                continue; // Host будет установлен автоматически

            // Пробуем добавить в заголовки запроса
            if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                // Если не получилось, добавляем в заголовки контента
                requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        // НЮАНС 6: КОПИРУЕМ BODY для POST/PUT/PATCH
        if (context.Request.Method == "POST" ||
            context.Request.Method == "PUT" ||
            context.Request.Method == "PATCH")
        {
            var streamContent = new StreamContent(context.Request.Body);

            if (context.Request.ContentType != null)
            {
                streamContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(context.Request.ContentType);
            }

            requestMessage.Content = streamContent;
        }

        // НЮАНС 7: ОТПРАВЛЯЕМ ЗАПРОС В ЦЕЛЕВОЙ СЕРВИС
        var responseMessage = await client.SendAsync(requestMessage);

        // НЮАНС 8: КОПИРУЕМ ОТВЕТ ОБРАТНО КЛИЕНТУ

        // Копируем статус код
        context.Response.StatusCode = (int)responseMessage.StatusCode;

        // Копируем заголовки ответа
        foreach (var header in responseMessage.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in responseMessage.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        // Удаляем проблемные заголовки (transfer-encoding конфликтует)
        context.Response.Headers.Remove("transfer-encoding");

        // Копируем тело ответа
        await responseMessage.Content.CopyToAsync(context.Response.Body);
    }
}
