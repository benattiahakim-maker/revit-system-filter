using System.Text.Json.Nodes;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Services;

/// <summary>Paramètre d'Activity (section <c>parameters</c> de <c>src/Packaging/definitions/&lt;année&gt;/activity.json</c>).</summary>
public sealed record ActivityParameter(string Name, string Verb, bool Zip, string? LocalName, bool Required, string? Description)
{
    /// <summary><c>get</c> et <c>read</c> sont des entrées ; les autres verbes (<c>put</c>, <c>post</c>, <c>patch</c>) des sorties.</summary>
    public bool IsInput => Verb is "get" or "read";

    public static IReadOnlyList<ActivityParameter> FromDefinition(JsonObject activity)
    {
        var parameters = activity["parameters"]?.AsObject() ?? throw new InvalidOperationException("Activity sans « parameters ».");
        return parameters.Select(p => new ActivityParameter(
                p.Key,
                p.Value?["verb"]?.GetValue<string>() ?? "get",
                p.Value?["zip"]?.GetValue<bool>() ?? false,
                p.Value?["localName"]?.GetValue<string>(),
                p.Value?["required"]?.GetValue<bool>() ?? false,
                p.Value?["description"]?.GetValue<string>()))
            .ToList();
    }
}

/// <summary>
/// Construction pure des arguments d'un WorkItem à partir des paramètres de l'Activity : chaque entrée reçoit l'URL
/// signée de lecture de son objet OSS, chaque sortie l'URL signée d'écriture. Les URL ne sortent jamais de cet objet
/// (elles ne sont ni journalisées ni affichées).
/// </summary>
public static class WorkItemArguments
{
    public static JsonObject Build(
        string activityId,
        IReadOnlyList<ActivityParameter> parameters,
        IReadOnlyDictionary<string, string> inputUrls,
        IReadOnlyDictionary<string, string> outputUrls)
    {
        var unknown = inputUrls.Keys.Concat(outputUrls.Keys).Where(k => parameters.All(p => p.Name != k)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException($"Argument(s) sans paramètre dans l'Activity : {string.Join(", ", unknown)}");
        }

        var arguments = new JsonObject();
        foreach (var parameter in parameters)
        {
            var urls = parameter.IsInput ? inputUrls : outputUrls;
            if (urls.TryGetValue(parameter.Name, out var url))
            {
                arguments[parameter.Name] = new JsonObject { ["url"] = url, ["verb"] = parameter.Verb };
            }
            else if (parameter.Required)
            {
                throw new InvalidOperationException($"Paramètre requis sans valeur : {parameter.Name} ({(parameter.IsInput ? "entrée" : "sortie")})");
            }
        }

        return new JsonObject { ["activityId"] = activityId, ["arguments"] = arguments };
    }
}
