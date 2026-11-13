using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JiraBackend;

public partial class JiraService(HttpClient http, Microsoft.Extensions.Options.IOptions<JiraOptions> options)
{
    private readonly JiraOptions _opt = options.Value;

    public async Task<TicketResponse> CreateTicketAsync(TicketRequest req, CancellationToken ct)
    {
        var fields = new Dictionary<string, object?>
        {
            ["project"] = new { key = string.IsNullOrWhiteSpace(req.ProjectKey) ? _opt.ProjectKey : req.ProjectKey },
            ["issuetype"] = new { name = string.IsNullOrWhiteSpace(req.IssueType) ? _opt.IssueType : req.IssueType },
            ["summary"] = req.Summary,
            ["description"] = ToAdf(req.Description)
        };
        if (!string.IsNullOrWhiteSpace(req.Priority))
            fields["priority"] = new { name = req.Priority };
        if (req.Labels is { Count: > 0 })
            fields["labels"] = req.Labels.Select(l => Whitespace().Replace(l, "_")).ToList();
        if (!string.IsNullOrWhiteSpace(req.ParentKey))
            fields["parent"] = new { key = req.ParentKey };
        // En Jira Cloud el sprint se asigna con el id numérico en el campo de sprint.
        if (req.SprintId is { } sprintId)
            fields[_opt.SprintFieldId] = sprintId;

        using var resp = await http.PostAsJsonAsync("/rest/api/3/issue", new { fields }, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new JiraException((int)resp.StatusCode, body);

        using var doc = JsonDocument.Parse(body);
        var key = doc.RootElement.GetProperty("key").GetString()!;
        return new TicketResponse(key, $"{_opt.BaseUrl.TrimEnd('/')}/browse/{key}");
    }

    /// <summary>Crea las subtareas en orden, bajo el issue padre. Devuelve las claves en el mismo orden.</summary>
    public async Task<List<TicketResponse>> CreateSubtasksAsync(string parentKey, SubtasksRequest req, CancellationToken ct)
    {
        var parentProject = parentKey.Split('-')[0];
        var created = new List<TicketResponse>();
        foreach (var s in req.Subtasks)
        {
            created.Add(await CreateTicketAsync(new TicketRequest(
                Summary: s.Summary,
                Description: string.IsNullOrWhiteSpace(s.Description) ? s.Summary : s.Description,
                Priority: null,
                Labels: req.Labels,
                IssueType: _opt.SubtaskIssueType,
                ProjectKey: parentProject,
                ParentKey: parentKey), ct));
        }
        return created;
    }

    /// <summary>Proyectos activos donde la cuenta puede crear issues.</summary>
    public async Task<List<ProjectResponse>> ListProjectsAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync("/rest/api/3/project/search?maxResults=100&action=create&orderBy=name", ct);
        return doc.RootElement.GetProperty("values").EnumerateArray()
            .Select(p => new ProjectResponse(p.GetProperty("key").GetString()!, p.GetProperty("name").GetString()!))
            .ToList();
    }

    /// <summary>Sprint activo del primer tablero del proyecto con sprints; null si no hay.</summary>
    public async Task<SprintResponse?> GetActiveSprintAsync(string projectKey, CancellationToken ct)
    {
        using var boards = await GetJsonAsync($"/rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(projectKey)}", ct);
        foreach (var board in boards.RootElement.GetProperty("values").EnumerateArray())
        {
            var boardId = board.GetProperty("id").GetInt32();
            using var resp = await http.GetAsync($"/rest/agile/1.0/board/{boardId}/sprint?state=active", ct);
            // Los tableros sin sprints (kanban) responden 400: se ignoran.
            if (!resp.IsSuccessStatusCode) continue;
            using var sprints = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var active = sprints.RootElement.GetProperty("values").EnumerateArray().FirstOrDefault();
            if (active.ValueKind == JsonValueKind.Object)
                return new SprintResponse(active.GetProperty("id").GetInt32(), active.GetProperty("name").GetString()!, "active");
        }
        return null;
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        using var resp = await http.GetAsync(path, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new JiraException((int)resp.StatusCode, body);
        return JsonDocument.Parse(body);
    }

    // Jira Cloud API v3 exige Atlassian Document Format para descripciones
    private static object ToAdf(string text) => new
    {
        type = "doc",
        version = 1,
        content = text.ReplaceLineEndings("\n").Split('\n').Select(line => string.IsNullOrWhiteSpace(line)
            ? (object)new { type = "paragraph" }
            : new { type = "paragraph", content = new[] { new { type = "text", text = line } } }).ToArray()
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
