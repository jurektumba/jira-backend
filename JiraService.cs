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
            ["project"] = new { key = _opt.ProjectKey },
            ["issuetype"] = new { name = string.IsNullOrWhiteSpace(req.IssueType) ? _opt.IssueType : req.IssueType },
            ["summary"] = req.Summary,
            ["description"] = ToAdf(req.Description)
        };
        if (!string.IsNullOrWhiteSpace(req.Priority))
            fields["priority"] = new { name = req.Priority };
        if (req.Labels is { Count: > 0 })
            fields["labels"] = req.Labels.Select(l => Whitespace().Replace(l, "_")).ToList();

        using var resp = await http.PostAsJsonAsync("/rest/api/3/issue", new { fields }, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new JiraException((int)resp.StatusCode, body);

        var key = JsonDocument.Parse(body).RootElement.GetProperty("key").GetString()!;
        return new TicketResponse(key, $"{_opt.BaseUrl.TrimEnd('/')}/browse/{key}");
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
