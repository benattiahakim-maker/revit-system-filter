using Egis.Revit.SystemFilter.Aps.DesignAutomation;
using Egis.Revit.SystemFilter.Aps.Packaging;

namespace Egis.Revit.SystemFilter.DaManager.Services;

/// <summary>
/// Construction pure des plans affichés avant chaque écriture. Les étapes reprennent l'algorithme de <c>deploy.py</c>
/// (src/Packaging/README.md, docs/PUBLISHING.md sections 5, 7 et 8) ; aucune URL signée ni secret n'y figure.
/// </summary>
public static class PlanBuilder
{
    private const string Test = DeploymentService.TestAlias;
    private const string Prod = DeploymentService.ProdAlias;

    public static OperationPlan Publish(string nickname, string? commit, IEnumerable<(string RevitVersion, ResourceState Bundle, ResourceState Activity, string ZipPath, long ZipSize)> items)
    {
        var steps = new List<string>
        {
            $"Publication en test (deploy.py test) depuis le commit {commit ?? "inconnu"} : ZIP et définitions doivent venir de ce même commit.",
            "GET /engines : arrêt avant toute écriture si un engine manque.",
        };
        foreach (var (version, bundle, activity, zip, size) in items)
        {
            steps.Add($"Revit {version} :");
            steps.Add($"  contrôle du ZIP {Path.GetFileName(zip)} ({size / 1024.0 / 1024.0:0.0} Mo)");
            steps.Add(bundle.Exists
                ? $"  POST /appbundles/{bundle.Id}/versions (nouvelle version après v{Max(bundle)}) + téléversement du ZIP"
                : $"  POST /appbundles (création de {bundle.Id}, v1) + téléversement du ZIP");
            steps.Add(AliasStep(DaResourceKind.AppBundle, bundle, Test, "nouvelle version"));
            steps.Add(activity.Exists
                ? $"  POST /activities/{activity.Id}/versions (nouvelle version après v{Max(activity)}, AppBundle {nickname}.{bundle.Id}+{Test})"
                : $"  POST /activities (création de {activity.Id}, AppBundle {nickname}.{bundle.Id}+{Test})");
            steps.Add(AliasStep(DaResourceKind.Activity, activity, Test, "nouvelle version"));
            steps.AddRange(QuotaWarnings(bundle, activity));
        }

        return new OperationPlan("Publier une nouvelle version en test", steps);
    }

    public static OperationPlan Promote(string nickname, string? commit, IEnumerable<(string RevitVersion, ResourceState Bundle, ResourceState Activity)> items)
    {
        var steps = new List<string>
        {
            $"Promotion en production (deploy.py promote) depuis le commit {commit ?? "inconnu"} : uniquement après une recette réussie, depuis le même commit que test.",
        };
        foreach (var (version, bundle, activity) in items)
        {
            var testVersion = bundle.AliasVersion(Test) ?? throw new InvalidOperationException($"{bundle.Id} : aucun alias {Test} à promouvoir");
            steps.Add($"Revit {version} :");
            steps.Add($"  {bundle.Id}+{Prod} -> v{testVersion} (actuellement {Show(bundle.AliasVersion(Prod))})");
            steps.Add($"  POST /activities/{activity.Id}/versions (AppBundle {nickname}.{bundle.Id}+{Prod}), puis {activity.Id}+{Prod} sur cette version (actuellement {Show(activity.AliasVersion(Prod))})");
            steps.Add(bundle.AliasVersion(Prod) is { } pb && activity.AliasVersion(Prod) is { } pa
                ? $"  Retour arrière possible : AppBundle v{pb}, Activity v{pa} (à recopier au journal)."
                : "  Première promotion : pas de retour arrière par alias possible.");
            steps.AddRange(QuotaWarnings(activity));
        }

        return new OperationPlan("Promouvoir test en prod", steps);
    }

    /// <summary>Création ou déplacement d'un alias (retour arrière compris). La version cible doit exister.</summary>
    public static OperationPlan SetAlias(ResourceState resource, string alias, int version)
    {
        if (!resource.Versions.Contains(version))
        {
            throw new InvalidOperationException($"{resource.Id} : la version v{version} n'existe pas.");
        }

        var current = resource.AliasVersion(alias);
        var steps = new List<string>
        {
            current is null
                ? $"POST /{resource.Kind.PathSegment()}/{resource.Id}/aliases : création de {alias} -> v{version}"
                : $"PATCH /{resource.Kind.PathSegment()}/{resource.Id}/aliases/{alias} : v{current} -> v{version}",
        };
        if (current is not null)
        {
            steps.Add($"Retour possible : repointer {alias} sur v{current}.");
        }

        if (alias == Prod)
        {
            steps.Add("Production : repointer aussi l'autre ressource (AppBundle et Activity) si nécessaire (docs/PUBLISHING.md section 8).");
        }

        return new OperationPlan($"Alias {resource.Id}+{alias}", steps);
    }

    public static OperationPlan Cleanup(CleanupPlan plan, IReadOnlyList<int>? journalVersions = null)
    {
        var journal = journalVersions is { Count: > 0 }
            ? $"Journal de publication : {string.Join(", ", journalVersions.Select(v => $"v{v}"))} protégée(s) automatiquement."
            : "Journal de publication : aucune version citée pour cette ressource.";
        return new($"Supprimer {plan.ToDelete.Count} version(s) de {plan.Id}", [.. plan.Describe(), journal, "Suppression définitive : une version supprimée ne peut plus servir de retour arrière."], IsDestructive: true);
    }

    /// <summary>
    /// Définition du nickname de l'application (<c>PATCH /forgeapps/me</c>). <paramref name="current"/> est la réponse
    /// de <c>GET /forgeapps/me</c> : l'ID client quand aucun nickname n'est défini, jamais affiché tel quel.
    /// <paramref name="ownedResources"/> : AppBundles et Activities de l'application déjà présents.
    /// </summary>
    public static OperationPlan SetNickname(string current, string target, string? clientId, int ownedResources)
    {
        if (!DesignAutomationClient.IsValidNickname(target))
        {
            throw new InvalidOperationException($"nickname cible « {target} » invalide : lettres, chiffres et « _ » uniquement ({DesignAutomationClient.MaxNicknameLength} caractères au plus).");
        }

        var hasNickname = HasNickname(current, clientId);
        var steps = new List<string>
        {
            hasNickname
                ? $"GET /forgeapps/me : nickname actuel « {current} »."
                : "GET /forgeapps/me : aucun nickname (APS renvoie l'ID client, non affiché).",
            $"Nickname cible : « {target} ».",
            $"PATCH /forgeapps/me, corps {{\"nickname\": \"{target}\"}}.",
            "ATTENTION : APS n'accepte ce changement que si l'application n'a encore AUCUNE donnée Design Automation (AppBundle, Activity…). À faire une seule fois, avant la première publication.",
            "Ensuite, le nickname n'est plus modifiable : seul DELETE /forgeapps/me, qui supprime toutes les données de l'application, le permettrait (DaManager ne le fait pas).",
            "Nickname unique chez APS : refus (409 Conflict) s'il est déjà pris par une autre application.",
        };
        if (ownedResources > 0)
        {
            steps.Add($"L'application a déjà {ownedResources} AppBundle(s) / Activity(ies) : APS refusera très probablement le changement (409).");
        }
        else if (hasNickname)
        {
            steps.Add($"Un nickname est déjà défini : le remplacer n'est possible que tant qu'aucune donnée n'a été publiée avec « {current} ».");
        }

        steps.Add($"Après succès : APS_NICKNAME={target} pour DaManager, WorkItemRunner et deploy.py (docs/OUTILS.md section 2).");
        return new OperationPlan("Définir le nickname de l'application APS", steps);
    }

    /// <summary>
    /// Vrai si la réponse de <c>GET /forgeapps/me</c> est un nickname et non l'ID client. Sans ID client connu
    /// (<c>APS_TOKEN</c> seul), une valeur alphanumérique d'au moins 32 caractères est traitée comme un ID client.
    /// </summary>
    public static bool HasNickname(string current, string? clientId) =>
        clientId is not null
            ? !string.Equals(current, clientId, StringComparison.Ordinal)
            : !(current.Length >= 32 && current.All(char.IsAsciiLetterOrDigit));

    private static string AliasStep(DaResourceKind kind, ResourceState state, string alias, string target) =>
        state.AliasVersion(alias) is { } current
            ? $"  PATCH /{kind.PathSegment()}/{state.Id}/aliases/{alias} : v{current} -> {target}"
            : $"  POST /{kind.PathSegment()}/{state.Id}/aliases : création de {alias} -> {target}";

    private static IEnumerable<string> QuotaWarnings(params ResourceState[] states) =>
        states.Where(s => s.Versions.Count >= ResourceState.VersionQuota - 10)
            .Select(s => $"  ATTENTION : {s.Id} a {s.Versions.Count} versions (quota {ResourceState.VersionQuota}) : nettoyer les anciennes versions.");

    private static int Max(ResourceState state) => state.Versions.Count == 0 ? 0 : state.Versions.Max();

    private static string Show(int? version) => version is null ? "aucun" : $"v{version}";
}
