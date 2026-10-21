using System.Text.Json;
using System.Text.Json.Nodes;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;

namespace Egis.Revit.SystemFilter.Aps.Packaging;

/// <summary>
/// Lecture des définitions <c>src/Packaging/definitions/&lt;année&gt;/{appbundle,activity}.json</c> (port de
/// <c>load_definition</c>, <c>definition_id</c> et <c>without_id</c> de <c>deploy.py</c>). Le texte est utilisé tel quel :
/// seuls <c>{{NICKNAME}}</c> et <c>{{APPBUNDLE_ALIAS}}</c> sont remplacés, <c>commandLine</c> n'est jamais modifié.
/// </summary>
public sealed class DefinitionLoader(string definitionsDirectory)
{
    public const string NicknamePlaceholder = "{{NICKNAME}}";
    public const string AppBundleAliasPlaceholder = "{{APPBUNDLE_ALIAS}}";

    public string DefinitionsDirectory { get; } = definitionsDirectory;

    /// <summary>Dossier <c>src/Packaging/definitions</c> d'un dépôt.</summary>
    public static DefinitionLoader ForRepository(string repositoryRoot) =>
        new(Path.Combine(repositoryRoot, "src", "Packaging", "definitions"));

    public string PathOf(string revitVersion, DaResourceKind kind) =>
        Path.Combine(DefinitionsDirectory, revitVersion, $"{kind.DefinitionName()}.json");

    /// <summary>Définition avec paramètres de gabarit remplacés ; erreur s'il en reste un.</summary>
    public JsonObject Load(string revitVersion, DaResourceKind kind, string? nickname = null, string? appBundleAlias = null)
    {
        var text = File.ReadAllText(PathOf(revitVersion, kind));
        if (nickname is not null)
        {
            text = text.Replace(NicknamePlaceholder, nickname, StringComparison.Ordinal);
        }

        if (appBundleAlias is not null)
        {
            text = text.Replace(AppBundleAliasPlaceholder, appBundleAlias, StringComparison.Ordinal);
        }

        var definition = Parse(text, revitVersion, kind);
        if (definition.ToJsonString().Contains("{{", StringComparison.Ordinal))
        {
            throw new ApsException($"definitions/{revitVersion}/{kind.DefinitionName()}.json : paramètre de gabarit non remplacé");
        }

        return definition;
    }

    /// <summary>Identifiant non qualifié (champ <c>id</c>), sans exiger les paramètres de gabarit.</summary>
    public string IdOf(string revitVersion, DaResourceKind kind)
    {
        var definition = Parse(File.ReadAllText(PathOf(revitVersion, kind)), revitVersion, kind);
        return definition["id"]?.GetValue<string>() ?? throw new ApsException($"definitions/{revitVersion}/{kind.DefinitionName()}.json : id absent");
    }

    /// <summary>Copie sans <c>id</c> (POST …/versions : « You can omit id from the request body »).</summary>
    public static JsonObject WithoutId(JsonObject definition)
    {
        var copy = definition.DeepClone().AsObject();
        copy.Remove("id");
        return copy;
    }

    private static JsonObject Parse(string text, string revitVersion, DaResourceKind kind)
    {
        try
        {
            return JsonNode.Parse(text)?.AsObject() ?? throw new ApsException($"definitions/{revitVersion}/{kind.DefinitionName()}.json : objet JSON attendu");
        }
        catch (JsonException exception)
        {
            throw new ApsException($"definitions/{revitVersion}/{kind.DefinitionName()}.json : JSON invalide ({exception.Message})", innerException: exception);
        }
    }
}
