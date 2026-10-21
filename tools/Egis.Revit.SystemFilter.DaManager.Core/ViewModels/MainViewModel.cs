using System.Collections.ObjectModel;
using System.Globalization;
using Egis.Revit.SystemFilter.Aps;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;
using Egis.Revit.SystemFilter.Aps.Packaging;
using Egis.Revit.SystemFilter.DaManager.Mvvm;
using Egis.Revit.SystemFilter.DaManager.Services;
using Egis.Revit.SystemFilter.Logging;

namespace Egis.Revit.SystemFilter.DaManager.ViewModels;

/// <summary>
/// Fenêtre principale de DaManager (EGR-19). Toute écriture APS suit le même parcours : plan construit par lectures seules,
/// affiché par <see cref="IConfirmationService"/>, exécution uniquement après confirmation, puis relecture de l'état.
/// Le journal est masqué par <see cref="SecretRedactor"/> ; aucun secret ni URL signée n'y est écrit.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private const string JournalFilter = "Journal JSON (*.json)|*.json|Table Markdown (*.md)|*.md";

    private readonly DaManagerOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IConfirmationService _confirmation;
    private readonly IFileDialogService? _files;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _uiContext;
    private DesignAutomationClient? _client;
    private DaManagerService? _service;
    private string _nickname;
    private string _repositoryRoot;
    private string _bundleDirectory;
    private ResourceItemViewModel? _selectedResource;
    private string _aliasName = DeploymentService.TestAlias;
    private string _aliasVersion = string.Empty;
    private int _keepLatest = 10;
    private string _protectedVersions = string.Empty;
    private string _journalPath;
    private string _journalStatus = string.Empty;
    private bool _isBusy;
    private string _status = "Prêt.";

    public MainViewModel(DaManagerOptions options, HttpClient httpClient, IConfirmationService confirmation, IFileDialogService? files = null, TimeProvider? time = null)
    {
        _options = options;
        _httpClient = httpClient;
        _confirmation = confirmation;
        _files = files;
        _time = time ?? TimeProvider.System;
        _uiContext = SynchronizationContext.Current;
        _nickname = options.Aps.Nickname ?? string.Empty;
        _repositoryRoot = options.RepositoryRoot ?? string.Empty;
        _bundleDirectory = options.BundleDirectory ?? string.Empty;
        _journalPath = options.JournalPath ?? string.Empty;
        foreach (var version in DeploymentService.SupportedVersions)
        {
            RevitVersions.Add(new RevitVersionOption(version, options.RevitVersions.Contains(version)));
        }

        RefreshCommand = new AsyncRelayCommand(() => RunAsync("Actualisation", RefreshCoreAsync), () => !IsBusy);
        PublishCommand = new AsyncRelayCommand(() => RunAsync("Publication en test", PublishCoreAsync), () => !IsBusy);
        PromoteCommand = new AsyncRelayCommand(() => RunAsync("Promotion en prod", PromoteCoreAsync), () => !IsBusy);
        SetAliasCommand = new AsyncRelayCommand(() => RunAsync("Alias", SetAliasCoreAsync), () => !IsBusy && SelectedResource is { State.Exists: true });
        CleanupCommand = new AsyncRelayCommand(() => RunAsync("Nettoyage des versions", CleanupCoreAsync), () => !IsBusy && SelectedResource is { State.Exists: true });
        BrowseBundleDirectoryCommand = new AsyncRelayCommand(() =>
        {
            BundleDirectory = _files?.PickFolder("Dossier des ZIP d'AppBundles", BundleDirectory) ?? BundleDirectory;
            return Task.CompletedTask;
        });
        BrowseRepositoryCommand = new AsyncRelayCommand(() =>
        {
            RepositoryRoot = _files?.PickFolder("Racine du dépôt EG_ROOM_TRACKER", RepositoryRoot) ?? RepositoryRoot;
            return Task.CompletedTask;
        });
        SetNicknameCommand = new AsyncRelayCommand(() => RunAsync("Définition du nickname", SetNicknameCoreAsync), () => !IsBusy);
        ImportJournalCommand = new AsyncRelayCommand(() => RunAsync("Import du journal", ImportJournalCoreAsync), () => !IsBusy);
        ExportJournalCommand = new AsyncRelayCommand(() => RunAsync("Export du journal", ExportJournalCoreAsync), () => !IsBusy);
        ReloadJournal();
        if (HasConfigurationBanner)
        {
            AddLog("warning", ConfigurationBanner);
        }
    }

    public string CredentialSource => _options.CredentialSource;

    /// <summary>
    /// Variables de configuration manquantes (noms seulement, jamais de valeur) : identifiants APS, nickname, racine du
    /// dépôt. Vide si la configuration est complète.
    /// </summary>
    public IReadOnlyList<string> MissingVariables
    {
        get
        {
            var missing = new List<string>(_options.Aps.MissingCredentialVariables());
            if (string.IsNullOrEmpty(Nickname))
            {
                missing.Add(ApsSettings.NicknameVariable);
            }

            if (!IsRepositoryRootValid)
            {
                missing.Add(DaManagerOptions.RepositoryRootVariable);
            }

            return missing;
        }
    }

    /// <summary>Bandeau affiché en haut de la fenêtre ; vide si rien ne manque.</summary>
    public string ConfigurationBanner => MissingVariables is { Count: > 0 } missing
        ? $"Configuration incomplète. Variables manquantes : {string.Join(", ", missing)}. " +
          (_options.Aps.HasCredentials ? string.Empty : "Les actions APS (Actualiser, Publier, Promouvoir, Alias, Nettoyage, Nickname) sont impossibles sans identifiants. ") +
          "Définir ces variables puis relancer l'outil (docs/OUTILS.md section 2) ; le journal de publication reste consultable."
        : string.Empty;

    public bool HasConfigurationBanner => ConfigurationBanner.Length > 0;

    /// <summary>Vrai dès qu'un client APS (et donc un fournisseur de jeton) a été créé : jamais au démarrage.</summary>
    internal bool HasApsClient => _client is not null;

    public string DaBaseUrl => _options.Aps.DaBaseUrl;

    public string? Commit => GitHead.Read(string.IsNullOrWhiteSpace(RepositoryRoot) ? null : RepositoryRoot);

    public ObservableCollection<RevitVersionOption> RevitVersions { get; } = [];

    public ObservableCollection<string> Engines { get; } = [];

    public ObservableCollection<string> AllAppBundles { get; } = [];

    public ObservableCollection<string> AllActivities { get; } = [];

    public ObservableCollection<ResourceItemViewModel> Resources { get; } = [];

    public ObservableCollection<LogEntry> Log { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand PublishCommand { get; }

    public AsyncRelayCommand PromoteCommand { get; }

    public AsyncRelayCommand SetAliasCommand { get; }

    public AsyncRelayCommand CleanupCommand { get; }

    public AsyncRelayCommand BrowseBundleDirectoryCommand { get; }

    public AsyncRelayCommand BrowseRepositoryCommand { get; }

    /// <summary>« Définir le nickname » : plan (nickname actuel, cible, avertissements), confirmation, PATCH /forgeapps/me.</summary>
    public AsyncRelayCommand SetNicknameCommand { get; }

    public AsyncRelayCommand ImportJournalCommand { get; }

    public AsyncRelayCommand ExportJournalCommand { get; }

    /// <summary>Lignes du journal de publication (docs/PUBLISHING.md section 8).</summary>
    public ObservableCollection<JournalEntry> Journal { get; } = [];

    /// <summary>Fichier du journal (JSON, ou table Markdown si <c>.md</c>) ; vide : pas de journal.</summary>
    public string JournalPath
    {
        get => _journalPath;
        set
        {
            if (SetProperty(ref _journalPath, value.Trim()))
            {
                ReloadJournal();
            }
        }
    }

    public string JournalStatus
    {
        get => _journalStatus;
        private set => SetProperty(ref _journalStatus, value);
    }

    public string Nickname
    {
        get => _nickname;
        set
        {
            if (SetProperty(ref _nickname, value.Trim()))
            {
                RaiseConfigurationChanged();
            }
        }
    }

    public string RepositoryRoot
    {
        get => _repositoryRoot;
        set
        {
            if (SetProperty(ref _repositoryRoot, value.Trim()))
            {
                _service = null; // définitions relues depuis le nouveau dépôt
                OnPropertyChanged(nameof(Commit));
                RaiseConfigurationChanged();
            }
        }
    }

    public string BundleDirectory
    {
        get => _bundleDirectory;
        set => SetProperty(ref _bundleDirectory, value.Trim());
    }

    public ResourceItemViewModel? SelectedResource
    {
        get => _selectedResource;
        set
        {
            if (SetProperty(ref _selectedResource, value))
            {
                if (value?.State.AliasVersion(AliasName) is { } current)
                {
                    AliasVersion = current.ToString(CultureInfo.InvariantCulture);
                }

                RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Alias à créer ou déplacer (test, prod…).</summary>
    public string AliasName
    {
        get => _aliasName;
        set => SetProperty(ref _aliasName, value.Trim());
    }

    /// <summary>Version cible de l'alias (retour arrière : version antérieure).</summary>
    public string AliasVersion
    {
        get => _aliasVersion;
        set => SetProperty(ref _aliasVersion, value.Trim());
    }

    /// <summary>Nombre de versions récentes conservées par le nettoyage (au moins 1).</summary>
    public int KeepLatest
    {
        get => _keepLatest;
        set => SetProperty(ref _keepLatest, Math.Max(1, value));
    }

    /// <summary>Versions protégées par le journal de publication, séparées par des virgules ou des espaces.</summary>
    public string ProtectedVersions
    {
        get => _protectedVersions;
        set => SetProperty(ref _protectedVersions, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    // ---- Opérations ----------------------------------------------------------------------------------------------

    private async Task RefreshCoreAsync()
    {
        var service = Service();
        var engines = await service.GetEnginesAsync();
        var bundles = await service.ListAsync(DaResourceKind.AppBundle);
        var activities = await service.ListAsync(DaResourceKind.Activity);
        var states = await service.ReadAllAsync(DeploymentService.SupportedVersions);
        var selected = SelectedResource?.Id;

        Replace(Engines, engines.Where(e => e.StartsWith("Autodesk.Revit+", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Replace(AllAppBundles, bundles.Order(StringComparer.Ordinal));
        Replace(AllActivities, activities.Order(StringComparer.Ordinal));
        Replace(Resources, states.Select(s => new ResourceItemViewModel(s)));
        SelectedResource = Resources.FirstOrDefault(r => r.Id == selected);

        var missing = SelectedVersions().Select(DesignAutomationClient.RevitEngine).Where(e => !engines.Contains(e)).ToList();
        AddLog("info", $"{Engines.Count} engine(s) Revit, {Resources.Count(r => r.State.Exists)}/{Resources.Count} ressource(s) du dépôt publiées.");
        if (missing.Count > 0)
        {
            AddLog("warning", $"engine(s) indisponible(s) : {string.Join(", ", missing)} : décocher ces versions (docs/PUBLISHING.md section 4).");
        }

        foreach (var near in Resources.Where(r => r.IsNearQuota))
        {
            AddLog("warning", $"{near.Id} : {near.VersionCount} versions (quota {ResourceState.VersionQuota}) : nettoyer les anciennes versions.");
        }
    }

    private async Task PublishCoreAsync()
    {
        var nickname = RequireNickname();
        var versions = RequireVersions();
        var service = Service();
        var plan = await service.PlanPublishAsync(nickname, versions, BundleDirectory, Commit);
        if (await ConfirmAsync(plan))
        {
            var before = await ReadStatesAsync(service, versions);
            await service.PublishAsync(nickname, versions, BundleDirectory);
            await RefreshCoreAsync();
            RecordPublication(DeploymentService.TestAlias, versions, before);
        }
    }

    private async Task PromoteCoreAsync()
    {
        var nickname = RequireNickname();
        var versions = RequireVersions();
        var service = Service();
        var plan = await service.PlanPromoteAsync(nickname, versions, Commit);
        if (await ConfirmAsync(plan))
        {
            var before = await ReadStatesAsync(service, versions);
            await service.PromoteAsync(nickname, versions);
            await RefreshCoreAsync();
            RecordPublication(DeploymentService.ProdAlias, versions, before);
        }
    }

    private static async Task<Dictionary<string, (ResourceState Bundle, ResourceState Activity)>> ReadStatesAsync(DaManagerService service, IReadOnlyList<string> versions)
    {
        var states = new Dictionary<string, (ResourceState Bundle, ResourceState Activity)>();
        foreach (var version in versions)
        {
            states[version] = (await service.ReadAsync(version, DaResourceKind.AppBundle), await service.ReadAsync(version, DaResourceKind.Activity));
        }

        return states;
    }

    /// <summary>
    /// Ajoute au journal une ligne par version publiée (<paramref name="alias"/> : test pour une publication, prod pour
    /// une promotion) : alias relus après l'écriture et version précédente de cet alias (retour arrière).
    /// </summary>
    private void RecordPublication(string alias, IReadOnlyList<string> versions, Dictionary<string, (ResourceState Bundle, ResourceState Activity)> before)
    {
        if (string.IsNullOrEmpty(JournalPath))
        {
            AddLog("warning", $"Aucun journal de publication configuré : noter la ligne {alias} à la main (docs/PUBLISHING.md section 8).");
            return;
        }

        var date = _time.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var commit = Commit?.Split(' ')[0];
        var entries = new List<JournalEntry>();
        foreach (var version in versions)
        {
            var bundle = Resources.Single(r => r.State.RevitVersion == version && r.State.Kind == DaResourceKind.AppBundle).State;
            var activity = Resources.Single(r => r.State.RevitVersion == version && r.State.Kind == DaResourceKind.Activity).State;
            var (oldBundle, oldActivity) = before[version];
            var previous = oldBundle.AliasVersion(alias) is null && oldActivity.AliasVersion(alias) is null
                ? null
                : new PreviousVersions(oldBundle.AliasVersion(alias), oldActivity.AliasVersion(alias));
            entries.Add(new JournalEntry(
                date,
                commit,
                version,
                alias,
                new AliasVersions(bundle.AliasVersion(DeploymentService.TestAlias), bundle.AliasVersion(DeploymentService.ProdAlias)),
                new AliasVersions(activity.AliasVersion(DeploymentService.TestAlias), activity.AliasVersion(DeploymentService.ProdAlias)),
                previous));
        }

        try
        {
            // Journal partagé : relu juste avant l'écriture, fusion par identifiant (lignes d'autres postes conservées).
            var journal = new PublicationJournal(entries).SaveMerged(JournalPath);
            ShowJournal(journal);
            foreach (var entry in entries)
            {
                AddLog("info", "Journal de publication : " + entry.Text);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            AddLog("error", $"Journal de publication non mis à jour ({exception.Message}) : noter la ligne à la main.");
        }
    }

    private Task ImportJournalCoreAsync()
    {
        var target = RequireJournalPath();
        if (_files?.OpenFile("Importer un journal de publication", JournalFilter) is not { } source)
        {
            return Task.CompletedTask;
        }

        var imported = PublicationJournal.Import(source, File.ReadAllText(source));
        var known = PublicationJournal.Load(target).Entries.Count;
        var merged = imported.SaveMerged(target);
        ShowJournal(merged);
        AddLog("info", $"Journal importé depuis {Path.GetFileName(source)} : {imported.Entries.Count} ligne(s) lue(s), {merged.Entries.Count - known} nouvelle(s).");
        return Task.CompletedTask;
    }

    private Task ExportJournalCoreAsync()
    {
        var journal = PublicationJournal.Load(RequireJournalPath());
        if (_files?.SaveFile("Exporter le journal de publication", JournalFilter, "publication-journal.json") is { } path)
        {
            journal.Save(path);
            AddLog("info", $"Journal exporté : {Path.GetFileName(path)} ({journal.Entries.Count} ligne(s)).");
        }

        return Task.CompletedTask;
    }

    private string RequireJournalPath() =>
        string.IsNullOrEmpty(JournalPath) ? throw new InvalidOperationException("Aucun fichier de journal : renseigner le chemin ou EGSF_PUBLICATION_JOURNAL.") : JournalPath;

    private void ReloadJournal()
    {
        if (string.IsNullOrEmpty(JournalPath))
        {
            ShowJournal(new PublicationJournal(), "aucun journal configuré");
            return;
        }

        try
        {
            ShowJournal(PublicationJournal.Load(JournalPath), File.Exists(JournalPath) ? null : "fichier absent, créé à la première promotion ou au premier import");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            ShowJournal(new PublicationJournal(), "illisible : " + exception.Message);
            AddLog("error", $"Journal de publication illisible : {exception.Message}");
        }
    }

    private void ShowJournal(PublicationJournal journal, string? note = null)
    {
        Replace(Journal, journal.Entries.Reverse());
        JournalStatus = $"{journal.Entries.Count} ligne(s)" + (note is null ? string.Empty : $" ({note})");
    }

    /// <summary>
    /// Nickname de l'application : lecture (GET /forgeapps/me et ressources existantes), plan, confirmation, puis
    /// PATCH /forgeapps/me et relecture. Le nickname cible est celui du champ Nickname.
    /// </summary>
    private async Task SetNicknameCoreAsync()
    {
        var target = Nickname;
        if (!DesignAutomationClient.IsValidNickname(target))
        {
            throw new InvalidOperationException($"Saisir le nickname voulu dans le champ Nickname : lettres, chiffres et « _ » uniquement ({DesignAutomationClient.MaxNicknameLength} caractères au plus).");
        }

        var client = Client();
        var current = await client.GetNicknameAsync();
        if (current == target)
        {
            AddLog("info", $"Le nickname de l'application est déjà « {target} » : rien à faire.");
            return;
        }

        // Ressources qualifiées par le nickname actuel (ou l'ID client) : celles de l'application.
        var owned = (await client.ListAsync(DaResourceKind.AppBundle)).Concat(await client.ListAsync(DaResourceKind.Activity))
            .Count(id => id.StartsWith(current + ".", StringComparison.Ordinal));
        var plan = PlanBuilder.SetNickname(current, target, _options.Aps.ClientId, owned);
        if (await ConfirmAsync(plan))
        {
            await client.SetNicknameAsync(target);
            if (await client.GetNicknameAsync() != target)
            {
                throw new ApsException("PATCH /forgeapps/me accepté, mais GET /forgeapps/me renvoie un autre nickname : vérifier sur APS avant de publier.");
            }

            AddLog("info", $"Nickname de l'application : « {target} ». Définir APS_NICKNAME={target} (variable utilisateur) pour les prochains lancements.");
        }
    }

    private async Task SetAliasCoreAsync()
    {
        var resource = SelectedResource?.State ?? throw new InvalidOperationException("Sélectionner une ressource.");
        if (string.IsNullOrEmpty(AliasName))
        {
            throw new InvalidOperationException("Nom d'alias obligatoire.");
        }

        if (!int.TryParse(AliasVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            throw new InvalidOperationException("Version cible invalide (entier attendu).");
        }

        var plan = PlanBuilder.SetAlias(resource, AliasName, version);
        if (await ConfirmAsync(plan))
        {
            await Service().SetAliasAsync(resource, AliasName, version);
            await RefreshCoreAsync();
        }
    }

    private async Task CleanupCoreAsync()
    {
        var resource = SelectedResource?.State ?? throw new InvalidOperationException("Sélectionner une ressource.");
        var service = Service();
        var manual = ParseVersions(ProtectedVersions);

        // Journal relu à chaque nettoyage ; illisible : rien n'est supprimé.
        var journal = string.IsNullOrEmpty(JournalPath) ? new PublicationJournal() : PublicationJournal.Load(JournalPath);
        var journalVersions = journal.ProtectedVersions(resource.RevitVersion, resource.Kind);
        if (string.IsNullOrEmpty(JournalPath) || !File.Exists(JournalPath))
        {
            AddLog("warning", "Aucun journal de publication : seules les versions saisies et les alias protègent les versions.");
        }

        var cleanup = await service.PlanCleanupAsync(resource, KeepLatest, manual.Concat(journalVersions));
        if (cleanup.IsEmpty)
        {
            AddLog("info", $"{resource.Id} : aucune version à supprimer ({cleanup.Kept.Count} conservée(s)).");
            return;
        }

        if (await ConfirmAsync(PlanBuilder.Cleanup(cleanup, journalVersions)))
        {
            var deleted = await service.CleanupAsync(cleanup);
            AddLog("info", $"{resource.Id} : {deleted.Count} version(s) supprimée(s).");
            await RefreshCoreAsync();
        }
    }

    // ---- Interne -------------------------------------------------------------------------------------------------

    private async Task<bool> ConfirmAsync(OperationPlan plan)
    {
        AddLog("plan", plan.Title + Environment.NewLine + plan.Text);
        var confirmed = await _confirmation.ConfirmAsync(plan);
        AddLog("info", confirmed ? $"Confirmé : {plan.Title}" : $"Annulé : {plan.Title} (rien n'a été écrit)");
        return confirmed;
    }

    private async Task RunAsync(string operation, Func<Task> action)
    {
        IsBusy = true;
        Status = operation + "…";
        try
        {
            await action();
            Status = operation + " : terminé.";
        }
        catch (Exception exception) when (exception is ApsException or InvalidOperationException or IOException or HttpRequestException or ArgumentException or TaskCanceledException)
        {
            AddLog("error", $"{operation} : {exception.Message}");
            Status = operation + " : échec (voir le journal).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private DaManagerService Service()
    {
        if (_service is not null)
        {
            return _service;
        }

        if (!IsRepositoryRootValid)
        {
            throw new InvalidOperationException("Racine du dépôt introuvable (src/Packaging/definitions) : la choisir ou définir EGSF_REPO_ROOT.");
        }

        _service = new DaManagerService(Client(), DefinitionLoader.ForRepository(RepositoryRoot), message => AddLog("info", message));
        return _service;
    }

    /// <summary>
    /// Client Automation API, créé à la première action APS seulement (jamais au démarrage). Ne dépend pas du dépôt :
    /// sert aussi au nickname.
    /// </summary>
    private DesignAutomationClient Client()
    {
        if (_client is not null)
        {
            return _client;
        }

        if (!_options.Aps.HasCredentials)
        {
            throw new InvalidOperationException($"Identifiants APS absents : {ApsSettings.MissingCredentialsMessage} Les définir puis relancer l'outil.");
        }

        _client = new DesignAutomationClient(_httpClient, _options.Aps.CreateTokenProvider(_httpClient, ApsScopes.DesignAutomation), _options.Aps.DaBaseUrl);
        return _client;
    }

    private bool IsRepositoryRootValid
    {
        get
        {
            try
            {
                return !string.IsNullOrWhiteSpace(RepositoryRoot) && Directory.Exists(Path.Combine(RepositoryRoot, "src", "Packaging", "definitions"));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    private void RaiseConfigurationChanged()
    {
        OnPropertyChanged(nameof(MissingVariables));
        OnPropertyChanged(nameof(ConfigurationBanner));
        OnPropertyChanged(nameof(HasConfigurationBanner));
    }

    private string RequireNickname() =>
        string.IsNullOrEmpty(Nickname) ? throw new InvalidOperationException("APS_NICKNAME est obligatoire (champ Nickname).") : Nickname;

    private List<string> SelectedVersions() => RevitVersions.Where(v => v.IsSelected).Select(v => v.Version).ToList();

    private List<string> RequireVersions()
    {
        var versions = SelectedVersions();
        return versions.Count == 0 ? throw new InvalidOperationException("Cocher au moins une version Revit.") : versions;
    }

    internal static IReadOnlyList<int> ParseVersions(string text)
    {
        var result = new List<int>();
        foreach (var part in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = part.TrimStart('v', 'V');
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            {
                throw new InvalidOperationException($"Version protégée invalide : « {part} ».");
            }

            result.Add(version);
        }

        return result;
    }

    /// <summary>Ajoute une entrée masquée au journal, sur le thread de l'interface s'il existe.</summary>
    private void AddLog(string level, string message)
    {
        var entry = new LogEntry(_time.GetUtcNow(), level, SecretRedactor.Redact(message));
        OnUi(() => Log.Add(entry));
    }

    private void OnUi(Action action)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            action();
        }
        else
        {
            _uiContext.Post(_ => action(), null);
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void RaiseCanExecuteChanged()
    {
        foreach (var command in new[] { RefreshCommand, PublishCommand, PromoteCommand, SetAliasCommand, CleanupCommand, SetNicknameCommand, ImportJournalCommand, ExportJournalCommand })
        {
            command?.RaiseCanExecuteChanged();
        }
    }
}
