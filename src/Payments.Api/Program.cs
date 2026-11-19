using Payments.Api;
using Payments.Api.Domain;
using Payments.Api.Idempotency;
using Payments.Api.Providers;
using Payments.Api.Reconciliation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
builder.Services.AddSingleton<SimulatedPaymentProvider>();
builder.Services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<SimulatedPaymentProvider>());
builder.Services.AddSingleton<PaymentService>();
builder.Services.AddSingleton<ReconciliationWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReconciliationWorker>());
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

app.MapPost("/payments", async (HttpRequest http, PaymentRequest body, PaymentService payments, CancellationToken ct) =>
{
    var key = http.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(key) || key.Length > 255)
    {
        return Results.Problem("An Idempotency-Key header (1-255 characters) is required.", statusCode: StatusCodes.Status400BadRequest);
    }

    if (body.AmountMinor <= 0 || body.Currency.Length != 3)
    {
        return Results.Problem("AmountMinor must be positive and Currency an ISO 4217 code.", statusCode: StatusCodes.Status400BadRequest);
    }

    return await payments.ProcessAsync(key, body, ct) switch
    {
        ProcessOutcome.Done { Result.Status: PaymentStatus.Unknown } d => Results.Accepted($"/payments/{d.Result.PaymentId}", d.Result),
        ProcessOutcome.Done d => d.Replayed ? Results.Ok(d.Result) : Results.Created($"/payments/{d.Result.PaymentId}", d.Result),
        ProcessOutcome.InProgress => Results.Problem("A request with this Idempotency-Key is still in progress. Retry later.", statusCode: StatusCodes.Status409Conflict),
        ProcessOutcome.KeyReusedWithDifferentRequest => Results.Problem("This Idempotency-Key was already used with a different request.", statusCode: StatusCodes.Status422UnprocessableEntity),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };
});

// Failure injection for demos only. Remove in any real deployment.
if (app.Environment.IsDevelopment())
{
    app.MapPost("/_chaos/lose-next-response", (SimulatedPaymentProvider p) =>
    {
        p.LoseNextResponse = true;
        return Results.NoContent();
    });
}

app.Run();

public partial class Program;
