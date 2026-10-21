using System.Text.Json.Nodes;

namespace Egis.Revit.SystemFilter.Aps.DesignAutomation;

/// <summary>
/// Paramètres de téléversement d'un AppBundle (<c>uploadParameters</c>) : formulaire S3 POST. Les champs
/// <see cref="FormData"/> sont des secrets temporaires et ne sont jamais restitués par <see cref="ToString"/>.
/// </summary>
public sealed record UploadParameters(string EndpointUrl, IReadOnlyList<KeyValuePair<string, string>> FormData)
{
    internal static UploadParameters FromJson(JsonNode node, string label)
    {
        var endpoint = node["endpointURL"]?.GetValue<string>() ?? throw new ApsException($"{label} : endpointURL absent.");
        var form = new List<KeyValuePair<string, string>>();
        if (node["formData"] is JsonObject formData)
        {
            foreach (var (key, value) in formData)
            {
                form.Add(new(key, value?.ToString() ?? string.Empty));
            }
        }

        return new UploadParameters(endpoint, form);
    }

    public override string ToString() => $"UploadParameters {{ EndpointUrl = ***, FormData = {FormData.Count} champ(s) }}";
}

/// <summary>Version créée par <c>POST /appbundles</c> ou <c>POST /appbundles/{id}/versions</c>.</summary>
public sealed record AppBundleVersion(int Version, UploadParameters UploadParameters);

/// <summary>Statut d'un WorkItem (<c>GET /workitems/{id}</c>). <see cref="ReportUrl"/> est une URL signée : à ne pas journaliser.</summary>
public sealed record WorkItemStatus(string Id, string Status, string? ReportUrl, JsonNode? Stats)
{
    public const string Pending = "pending";
    public const string InProgress = "inprogress";
    public const string Success = "success";

    /// <summary>Statut terminal : ni <c>pending</c>, ni <c>inprogress</c>.</summary>
    public bool IsCompleted => Status is not (Pending or InProgress);

    public bool IsSuccess => Status == Success;

    internal static WorkItemStatus FromJson(JsonNode? node, string label)
    {
        var id = node?["id"]?.GetValue<string>() ?? throw new ApsException($"{label} : id absent.");
        var status = node["status"]?.GetValue<string>() ?? throw new ApsException($"{label} : status absent.");
        return new WorkItemStatus(id, status, node["reportUrl"]?.GetValue<string>(), node["stats"]?.DeepClone());
    }

    public override string ToString() => $"WorkItemStatus {{ Id = {Id}, Status = {Status}, ReportUrl = {(ReportUrl is null ? "<absent>" : "***")} }}";
}
