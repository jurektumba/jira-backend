using System.ComponentModel.DataAnnotations;

namespace JiraBackend;

public class JiraOptions
{
    public string BaseUrl { get; set; } = "";
    public string ProjectKey { get; set; } = "";
    public string IssueType { get; set; } = "Task";
    public string Email { get; set; } = "";
    public string ApiToken { get; set; } = "";
}

public record TicketRequest(
    [property: Required, MaxLength(250)] string Summary,
    [property: Required] string Description,
    string? Priority,
    List<string>? Labels,
    string? IssueType = null);

public record TicketResponse(string Key, string Url);

public class JiraException(int status, string detail) : Exception("Jira rechazó la solicitud")
{
    public int Status { get; } = status;
    public string Detail { get; } = detail;
}
