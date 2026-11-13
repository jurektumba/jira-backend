using System.ComponentModel.DataAnnotations;

namespace JiraBackend;

public class JiraOptions
{
    public string BaseUrl { get; set; } = "";
    /// <summary>Proyecto por defecto si la petición no indica uno.</summary>
    public string ProjectKey { get; set; } = "";
    public string IssueType { get; set; } = "Tarea";
    /// <summary>Tipo de issue para subtareas (en GRUP es "Subtask").</summary>
    public string SubtaskIssueType { get; set; } = "Subtask";
    /// <summary>Campo de sprint de Jira Cloud (en este sitio, customfield_10020).</summary>
    public string SprintFieldId { get; set; } = "customfield_10020";
    public string Email { get; set; } = "";
    public string ApiToken { get; set; } = "";
}

public class SecurityOptions
{
    /// <summary>Si se define, todas las rutas /api/* salvo /api/health exigen el header X-Api-Key.</summary>
    public string ApiKey { get; set; } = "";
}

public record TicketRequest(
    [property: Required, MaxLength(250)] string Summary,
    [property: Required] string Description,
    string? Priority,
    List<string>? Labels,
    string? IssueType = null,
    // Proyecto destino; si falta, se usa Jira:ProjectKey.
    string? ProjectKey = null,
    // Issue padre (obligatorio para subtareas).
    string? ParentKey = null,
    // Sprint al que se agrega el issue; si falta, queda en el backlog.
    int? SprintId = null,
    // Fecha de vencimiento (yyyy-MM-dd); en OCD se usa como fecha de pase.
    string? DueDate = null,
    // Issue con el que se crea un enlace "Relates" (ej. el OCD con su Historia).
    string? RelatesTo = null,
    // Estado al que se mueve el issue justo después de crearlo (ej. "Validación").
    string? InitialStatus = null);

public record TransitionRequest([property: Required] string Status);

public record CommentRequest([property: Required] string Body);

public record LinkRequest([property: Required] string TargetKey, string? Type = "Relates");

public record UpdateTicketRequest(string? DueDate);

public record TicketStatusResponse(string Key, string Url, string Summary, string Status, string? DueDate);

public record SubtaskItem([property: Required, MaxLength(250)] string Summary, string? Description);

public record SubtasksRequest([property: Required] List<SubtaskItem> Subtasks, List<string>? Labels);

public record TicketResponse(string Key, string Url);

public record ProjectResponse(string Key, string Name);

public record SprintResponse(int Id, string Name, string State);

public class JiraException(int status, string detail) : Exception("Jira rechazó la solicitud")
{
    public int Status { get; } = status;
    public string Detail { get; } = detail;
}
