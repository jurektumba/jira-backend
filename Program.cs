using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using JiraBackend;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Credenciales vía variables de entorno: Jira__Email, Jira__ApiToken (y opcional Security__ApiKey)
builder.Services.Configure<JiraOptions>(builder.Configuration.GetSection("Jira"));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));
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

var jiraOpt = app.Services.GetRequiredService<IOptions<JiraOptions>>().Value;
if (string.IsNullOrWhiteSpace(jiraOpt.Email) || string.IsNullOrWhiteSpace(jiraOpt.ApiToken))
    app.Logger.LogError("Faltan Jira__Email o Jira__ApiToken: las llamadas a Jira fallarán con 401.");

var apiKey = app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value.ApiKey;
if (string.IsNullOrWhiteSpace(apiKey))
    app.Logger.LogWarning("Security__ApiKey no está definido: la API acepta llamadas sin autenticación.");

app.UseCors();

// Clave compartida opcional: protege todo /api/* salvo /api/health.
app.Use(async (ctx, next) =>
{
    if (!string.IsNullOrWhiteSpace(apiKey)
        && ctx.Request.Path.StartsWithSegments("/api")
        && !ctx.Request.Path.StartsWithSegments("/api/health")
        && !HttpMethods.IsOptions(ctx.Request.Method)
        && ctx.Request.Headers["X-Api-Key"] != apiKey)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { error = "Falta o es inválido el header X-Api-Key" });
        return;
    }
    await next();
});

app.MapGet("/api/health", () => new { status = "ok" });

// Crea un issue (Historia, Tarea, Subtask…). Opcionales: projectKey, parentKey, sprintId.
app.MapPost("/api/tickets", (TicketRequest req, JiraService jira, CancellationToken ct) => JiraCall(async () =>
{
    if (!IsValid(req) || string.IsNullOrWhiteSpace(req.Summary) || string.IsNullOrWhiteSpace(req.Description) || req.Summary.Length > 250)
        return Results.BadRequest(new { error = "summary y description son obligatorios (summary máx. 250)" });
    var created = await jira.CreateTicketAsync(req, ct);
    return Results.Created(created.Url, created);
}));

// Crea varias subtareas bajo un issue, en orden.
app.MapPost("/api/tickets/{parentKey}/subtasks", (string parentKey, SubtasksRequest req, JiraService jira, CancellationToken ct) => JiraCall(async () =>
{
    if (!IsValid(req) || req.Subtasks.Count == 0 || req.Subtasks.Any(s => !IsValid(s) || string.IsNullOrWhiteSpace(s.Summary)))
        return Results.BadRequest(new { error = "subtasks debe tener al menos un elemento con summary (máx. 250)" });
    var created = await jira.CreateSubtasksAsync(parentKey, req, ct);
    return Results.Ok(created);
}));

// Proyectos donde la cuenta puede crear issues.
app.MapGet("/api/projects", (JiraService jira, CancellationToken ct) =>
    JiraCall(async () => Results.Ok(await jira.ListProjectsAsync(ct))));

// Sprint activo del proyecto; responde null si no hay (la historia irá al backlog).
app.MapGet("/api/projects/{key}/active-sprint", (string key, JiraService jira, CancellationToken ct) =>
    JiraCall(async () => await jira.GetActiveSprintAsync(key, ct) is { } sprint
        ? Results.Json(sprint)
        : Results.Content("null", "application/json")));

app.Run();

static bool IsValid(object o) => Validator.TryValidateObject(o, new ValidationContext(o), new List<ValidationResult>(), true);

// Traduce los errores de Jira a 502 con el estado y el detalle que devolvió Jira.
static async Task<IResult> JiraCall(Func<Task<IResult>> action)
{
    try
    {
        return await action();
    }
    catch (JiraException e)
    {
        var detail = e.Detail.Length > 2000 ? e.Detail[..2000] : e.Detail;
        return Results.Json(new { error = e.Message, jiraStatus = e.Status, detail }, statusCode: 502);
    }
}
