using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using JiraBackend;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Credenciales vía variables de entorno: Jira__Email, Jira__ApiToken
builder.Services.Configure<JiraOptions>(builder.Configuration.GetSection("Jira"));
builder.Services.AddHttpClient<JiraService>((sp, client) =>
{
    var o = sp.GetRequiredService<IOptions<JiraOptions>>().Value;
    client.BaseAddress = new Uri(o.BaseUrl);
    var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.Email}:{o.ApiToken}"));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().WithMethods("GET", "POST")));

var app = builder.Build();
app.UseCors();

app.MapGet("/api/health", () => new { status = "ok" });

app.MapPost("/api/tickets", async (TicketRequest req, JiraService jira, CancellationToken ct) =>
{
    var errors = new List<ValidationResult>();
    if (!Validator.TryValidateObject(req, new ValidationContext(req), errors, true) ||
        string.IsNullOrWhiteSpace(req.Summary) || string.IsNullOrWhiteSpace(req.Description) || req.Summary.Length > 250)
        return Results.BadRequest(new { error = "summary y description son obligatorios (summary máx. 250)" });

    try
    {
        var created = await jira.CreateTicketAsync(req, ct);
        return Results.Created(created.Url, created);
    }
    catch (JiraException e)
    {
        return Results.Json(new { error = e.Message, jiraStatus = e.Status, detail = e.Detail }, statusCode: 502);
    }
});

app.Run();
