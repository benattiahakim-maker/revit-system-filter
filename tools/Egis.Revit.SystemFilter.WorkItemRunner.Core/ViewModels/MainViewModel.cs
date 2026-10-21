using System.Collections.ObjectModel;
using Egis.Revit.SystemFilter.Aps;
using Egis.Revit.SystemFilter.Aps.DesignAutomation;
using Egis.Revit.SystemFilter.Aps.Oss;
using Egis.Revit.SystemFilter.Aps.Packaging;
using Egis.Revit.SystemFilter.Logging;
using Egis.Revit.SystemFilter.WorkItemRunner.Mvvm;
using Egis.Revit.SystemFilter.WorkItemRunner.Services;

namespace Egis.Revit.SystemFilter.WorkItemRunner.ViewModels;

/// <summary>Entrée du journal (déjà masquée).</summary>
public sealed record LogEntry(DateTimeOffset Time, string Level, string Message)
{
    public string TimeText => Time.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Fenêtre principale de WorkItemRunner (EGR-20). Un lancement suit toujours le même parcours : contrôles locaux et
/// lectures seules, plan affiché et confirmé (exécution facturée), puis téléversement, soumission, suivi et
/// récupération des sorties. Aucun secret ni URL signée n'est affiché ou journalisé.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly RunnerOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IConfirmationService _confirmation;
    private readonly IFileDialogService? _files;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _uiContext;
    private readonly RunHistory _history;
    private readonly LastConfigStore _lastConfig;
    private WorkItemRunnerService? _service;
    private ResultReader? _results;
    private ActiveRun? _activeRun;
    private CancellationTokenSource? _follow;
    private string _nickname;
    private string _bucketKey;
    private string _repositoryRoot;
    private string _selectedRevitVersion = "2026";
    private string _selectedAlias = DeploymentService.TestAlias;
    private string _zipPath = string.Empty;
    private string _settingsText = string.Empty;
    private string _settingsValidation = string.Empty;
    private int _pollIntervalSeconds = 10;
    private string _workItemStatus = "—";
    private RunRecord? _selectedRun;
    private ResultView? _result;
    private string _reportText = string.Empty;
    private bool _isBusy;
    private string _status = "Prêt.";

    public MainViewModel(RunnerOptions options, HttpClient httpClient, IConfirmationService confirmation, IFileDialogService? files = null, TimeProvider? time = null)
    {
        _options = options;
        _httpClient = httpClient;
        _confirmation = confirmation;
        _files = files;
        _time = time ?? TimeProvider.System;
        _uiContext = SynchronizationContext.Current;
        _nickname = options.Aps.Nickname ?? string.Empty;
        _bucketKey = options.BucketKey ?? string.Empty;
        _repositoryRoot = options.RepositoryRoot ?? string.Empty;
        _history = new RunHistory(options.DataDirectory); // historique local : aucun client APS au démarrage
        _lastConfig = new LastConfigStore(options.DataDirectory);

        RunCommand = new AsyncRelayCommand(() => RunAsync("Lancement", RunCoreAsync), () => !IsBusy);
        StopFollowingCommand = new AsyncRelayCommand(StopFollowingAsync, () => _follow is not null);
        CancelWorkItemCommand = new AsyncRelayCommand(CancelWorkItemAsync, () => _activeRun is not null && _follow is not null);
        RefreshHistoryCommand = new AsyncRelayCommand(() => RunAsync("Historique", () => { ReloadHistory(); return Task.CompletedTask; }), () => !IsBusy);
        ExportRunCommand = new AsyncRelayCommand(() => RunAsync("Export", ExportCoreAsync), () => !IsBusy && SelectedRun is not null);
        RecoverCommand = new AsyncRelayCommand(() => RunAsync("Récupération", RecoverCoreAsync), () => !IsBusy && _follow is null && SelectedRun is { WorkItemId: not null });
        BrowseZipCommand = new AsyncRelayCommand(() =>
        {
            ZipPath = _files?.OpenFile("ZIP de la maquette", "ZIP (*.zip)|*.zip", null) ?? ZipPath;
            SaveLastConfig();
            return Task.CompletedTask;
        });
        OpenSettingsCommand = new AsyncRelayCommand(() => RunAsync("settings.json", () =>
        {
            if (_files?.OpenFile("settings.json", "JSON (*.json)|*.json", null) is { } path)
            {
                SettingsText = File.ReadAllText(path);
                SaveLastConfig();
            }

            return Task.CompletedTask;
        }));
        LoadExampleSettingsCommand = new AsyncRelayCommand(() => RunAsync("Exemple", () =>
        {
            SettingsText = File.ReadAllText(Path.Combine(RequireRepositoryRoot(), "docs", "examples", "settings.example.json"));
            return Task.CompletedTask;
        }));
    }

    public string CredentialSource => _options.CredentialSource;

    /// <summary>
    /// Variables de configuration manquantes (noms seulement, jamais de valeur) : identifiants APS, nickname, racine du
    /// dépôt, bucket. Vide si la configuration est complète.
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
                missing.Add(RunnerOptions.RepositoryRootVariable);
            }

            if (string.IsNullOrEmpty(BucketKey))
            {
                missing.Add(RunnerOptions.BucketVariable);
            }

            return missing;
        }
    }

    /// <summary>Bandeau affiché en haut de la fenêtre ; vide si rien ne manque.</summary>
    public string ConfigurationBanner => MissingVariables is { Count: > 0 } missing
        ? $"Configuration incomplète. Variables manquantes : {string.Join(", ", missing)}. " +
          (_options.Aps.HasCredentials ? string.Empty : "Les actions APS (Lancer, Récupérer, Annuler) sont impossibles sans identifiants. ") +
          "Définir ces variables puis relancer l'outil (docs/OUTILS.md section 2) ; l'historique local reste consultable."
        : string.Empty;

    public bool HasConfigurationBanner => ConfigurationBanner.Length > 0;

    /// <summary>Vrai dès qu'un client APS (et donc un fournisseur de jeton) a été créé : jamais au démarrage.</summary>
    internal bool HasApsClient => _service is not null;

    public IReadOnlyList<string> RevitVersions => RunnerOptions.RevitVersions;

    public IReadOnlyList<string> Aliases { get; } = [DeploymentService.TestAlias, DeploymentService.ProdAlias];

    public ObservableCollection<RunRecord> History { get; } = [];

    public ObservableCollection<LogEntry> Log { get; } = [];

    public AsyncRelayCommand RunCommand { get; }

    public AsyncRelayCommand StopFollowingCommand { get; }

    public AsyncRelayCommand CancelWorkItemCommand { get; }

    public AsyncRelayCommand RefreshHistoryCommand { get; }

    public AsyncRelayCommand ExportRunCommand { get; }

    /// <summary>« Récupérer » : relit le statut du WorkItem sélectionné et télécharge rapport et sorties (après redémarrage aussi).</summary>
    public AsyncRelayCommand RecoverCommand { get; }

    public AsyncRelayCommand BrowseZipCommand { get; }

    public AsyncRelayCommand OpenSettingsCommand { get; }

    public AsyncRelayCommand LoadExampleSettingsCommand { get; }

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

    /// <summary>Bucket OSS (APS_BUCKET) ; objets transitoires, supprimés par APS après 24 h.</summary>
    public string BucketKey
    {
        get => _bucketKey;
        set
        {
            if (SetProperty(ref _bucketKey, value.Trim()))
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
                _service = null;
                _results = null;
                RaiseConfigurationChanged();
            }
        }
    }

    public string SelectedRevitVersion
    {
        get => _selectedRevitVersion;
        set => SetProperty(ref _selectedRevitVersion, value);
    }

    public string SelectedAlias
    {
        get => _selectedAlias;
        set => SetProperty(ref _selectedAlias, value);
    }

    public string ZipPath
    {
        get => _zipPath;
        set => SetProperty(ref _zipPath, value.Trim());
    }

    /// <summary>Contenu de settings.json (éditeur) ; validé à chaque modification avec les règles de l'extracteur.</summary>
    public string SettingsText
    {
        get => _settingsText;
        set
        {
            if (SetProperty(ref _settingsText, value))
            {
                var check = InputChecks.CheckSettings(value);
                SettingsValidation = check.Errors.Concat(check.Warnings).FirstOrDefault() ?? "settings.json valide.";
            }
        }
    }

    public string SettingsValidation
    {
        get => _settingsValidation;
        private set => SetProperty(ref _settingsValidation, value);
    }

    public int PollIntervalSeconds
    {
        get => _pollIntervalSeconds;
        set => SetProperty(ref _pollIntervalSeconds, Math.Max(0, value));
    }

    public string WorkItemStatus
    {
        get => _workItemStatus;
        private set => SetProperty(ref _workItemStatus, value);
    }

    public RunRecord? SelectedRun
    {
        get => _selectedRun;
        set
        {
            if (SetProperty(ref _selectedRun, value))
            {
                ShowRun(value);
                ExportRunCommand.RaiseCanExecuteChanged();
                RecoverCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>result.json du lancement sélectionné (bilan, pièces, anomalies, écarts au schéma).</summary>
    public ResultView? Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    public string ReportText
    {
        get => _reportText;
        private set => SetProperty(ref _reportText, value);
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

    /// <summary>
    /// Démarrage : historique local uniquement, sans client APS ni jeton. Ne lève jamais : un historique illisible ou
    /// une configuration incomplète sont signalés dans le journal et le bandeau.
    /// </summary>
    public void Initialize()
    {
        RestoreLastConfig();
        if (HasConfigurationBanner)
        {
            AddLog("warning", ConfigurationBanner);
        }

        try
        {
            ReloadHistory();
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            AddLog("warning", $"Historique local illisible ({_history.RunsDirectory}) : {exception.Message}");
        }
    }

    /// <summary>Rappelle la dernière configuration de lancement (version Revit, alias, ZIP, settings.json).</summary>
    private void RestoreLastConfig()
    {
        if (_lastConfig.Load() is not { } last)
        {
            return;
        }

        if (last.RevitVersion is { } version && RunnerOptions.RevitVersions.Contains(version))
        {
            SelectedRevitVersion = version;
        }

        if (!string.IsNullOrWhiteSpace(last.Alias))
        {
            SelectedAlias = last.Alias;
        }

        if (!string.IsNullOrWhiteSpace(last.ZipPath))
        {
            ZipPath = last.ZipPath;
        }

        if (!string.IsNullOrWhiteSpace(last.SettingsText))
        {
            SettingsText = last.SettingsText;
        }
    }

    /// <summary>
    /// Mémorise la configuration choisie par l'utilisateur. Appelée seulement après un choix de fichier ou la
    /// confirmation d'un lancement : un plan refusé n'écrit rien.
    /// </summary>
    private void SaveLastConfig() =>
        _lastConfig.Save(new LastConfig(SelectedRevitVersion, SelectedAlias, ZipPath, SettingsText));

    // ---- Opérations ----------------------------------------------------------------------------------------------

    private async Task RunCoreAsync()
    {
        var service = Service();
        if (string.IsNullOrEmpty(Nickname))
        {
            throw new InvalidOperationException("APS_NICKNAME est obligatoire (champ Nickname).");
        }

        var interval = TimeSpan.FromSeconds(PollIntervalSeconds);
        var request = new RunRequest(SelectedRevitVersion, SelectedAlias, Nickname, ZipPath, SettingsText);
        var prepared = await service.PrepareAsync(request, BucketKey, interval);
        AddLog("plan", prepared.Plan.Title + Environment.NewLine + prepared.Plan.Text);
        if (!await _confirmation.ConfirmAsync(prepared.Plan))
        {
            AddLog("info", "Annulé : aucun fichier téléversé, aucun WorkItem soumis.");
            return;
        }

        AddLog("info", "Confirmé : lancement du WorkItem.");
        SaveLastConfig(); // la configuration d'un lancement confirmé est rappelée au prochain démarrage
        var run = await service.SubmitAsync(prepared, Progress);
        _activeRun = run;
        _follow = new CancellationTokenSource();
        RaiseCanExecuteChanged();
        try
        {
            WorkItemStatus = run.Record.WorkItemStatus ?? "—";
            var status = await service.WaitAsync(run, interval, new Callback<WorkItemStatus>(s => OnUi(() => WorkItemStatus = s.Status)), _follow.Token);
            WorkItemStatus = status.Status;
            AddLog(status.IsSuccess ? "info" : "warning", $"WorkItem {status.Id} terminé : {status.Status}.");
            var (record, result, report) = await service.CollectAsync(run, status, Progress);
            ReloadHistory();
            SelectedRun = History.FirstOrDefault(r => r.RunId == record.RunId);
            if (result is { IsSchemaValid: false })
            {
                AddLog("warning", $"result.json non conforme au schéma : {result.SchemaErrors.Count} écart(s).");
            }
        }
        catch (OperationCanceledException)
        {
            AddLog("info", $"Suivi arrêté ; le WorkItem {run.Record.WorkItemId} continue côté APS (statut conservé 3 jours).");
            ReloadHistory();
        }
        finally
        {
            _follow?.Dispose();
            _follow = null;
            _activeRun = null;
            RaiseCanExecuteChanged();
        }
    }

    private Task StopFollowingAsync()
    {
        _follow?.Cancel();
        return Task.CompletedTask;
    }

    private async Task CancelWorkItemAsync()
    {
        if (_activeRun is not { } run)
        {
            return;
        }

        var plan = new OperationPlan("Annuler le WorkItem", [$"DELETE /workitems/{run.Record.WorkItemId} : le WorkItem en attente ou en cours est annulé.", "Les sorties ne seront probablement pas produites."], IsDestructive: true);
        AddLog("plan", plan.Title + Environment.NewLine + plan.Text);
        if (!await _confirmation.ConfirmAsync(plan))
        {
            AddLog("info", "Annulation du WorkItem abandonnée.");
            return;
        }

        try
        {
            await Service().CancelAsync(run);
            AddLog("info", $"Annulation demandée pour {run.Record.WorkItemId}.");
        }
        catch (ApsException exception)
        {
            AddLog("error", $"Annulation : {exception.Message}");
        }
    }

    private async Task RecoverCoreAsync()
    {
        var run = SelectedRun ?? throw new InvalidOperationException("Sélectionner un lancement.");
        AddLog("info", $"Récupération du lancement {run.RunId} (WorkItem {run.WorkItemId}) : statut, rapport et sorties ; URL signées de lecture redemandées à APS.");
        var (record, result, _) = await Service().RecoverAsync(run, Progress);
        WorkItemStatus = record.WorkItemStatus ?? "—";
        ReloadHistory();
        SelectedRun = History.FirstOrDefault(r => r.RunId == record.RunId);
        ShowRun(SelectedRun);
        if (result is { IsSchemaValid: false })
        {
            AddLog("warning", $"result.json non conforme au schéma : {result.SchemaErrors.Count} écart(s).");
        }
    }

    private Task ExportCoreAsync()
    {
        var run = SelectedRun ?? throw new InvalidOperationException("Sélectionner un lancement.");
        if (_files?.SaveFile("Exporter le lancement", "ZIP (*.zip)|*.zip", $"workitem-{run.RunId}.zip") is { } path)
        {
            _history.Export(run.RunId, path);
            AddLog("info", $"Lancement {run.RunId} exporté : {Path.GetFileName(path)}");
        }

        return Task.CompletedTask;
    }

    // ---- Interne -------------------------------------------------------------------------------------------------

    /// <summary>Affiche un lancement depuis les fichiers locaux (sans APS ; sans dépôt, result.json n'est pas validé).</summary>
    private void ShowRun(RunRecord? run)
    {
        Result = null;
        ReportText = string.Empty;
        if (run is null)
        {
            return;
        }

        try
        {
            var directory = _history.DirectoryOf(run.RunId);
            // Sortie ZIP décompressée dans result/ ; lancements antérieurs : result.json à la racine du lancement.
            var zippedResult = Path.Combine(directory, WorkItemRunnerService.ResultParameter, WorkItemRunnerService.ResultLocalName);
            var resultPath = File.Exists(zippedResult) ? zippedResult : Path.Combine(directory, WorkItemRunnerService.ResultLocalName);
            var reportPath = Path.Combine(directory, RunHistory.ReportFileName);
            var trcPath = Path.Combine(directory, WorkItemRunnerService.ResultParameter, WorkItemRunnerService.TrcLocalName);
            if (File.Exists(resultPath) && Results() is { } reader)
            {
                Result = reader.Read(File.ReadAllText(resultPath));
            }
            else if (File.Exists(trcPath) && Results() is { } trcReader)
            {
                Result = trcReader.ReadTrc(File.ReadAllText(trcPath));
            }

            ReportText = File.Exists(reportPath) ? SecretRedactor.Redact(File.ReadAllText(reportPath)) : "(rapport non téléchargé)";
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            AddLog("warning", $"Lancement {run.RunId} illisible : {exception.Message}");
        }
    }

    /// <summary>Lecteur de result.json (schéma du dépôt) ; null tant que la racine du dépôt est introuvable.</summary>
    private ResultReader? Results() =>
        _results ??= IsRepositoryRootValid ? ResultReader.ForRepository(RepositoryRoot) : null;

    private void ReloadHistory()
    {
        var records = _history.Load();
        var selected = SelectedRun?.RunId;
        History.Clear();
        foreach (var record in records)
        {
            History.Add(record);
        }

        SelectedRun = History.FirstOrDefault(r => r.RunId == selected);
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
        catch (Exception exception) when (exception is ApsException or InvalidOperationException or IOException or HttpRequestException or ArgumentException or UnauthorizedAccessException)
        {
            AddLog("error", $"{operation} : {exception.Message}");
            Status = operation + " : échec (voir le journal).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Services APS, créés à la première action APS seulement (jamais au démarrage).</summary>
    private WorkItemRunnerService Service()
    {
        if (_service is not null)
        {
            return _service;
        }

        if (!_options.Aps.HasCredentials)
        {
            throw new InvalidOperationException($"Identifiants APS absents : {ApsSettings.MissingCredentialsMessage} Les définir puis relancer l'outil.");
        }

        var root = RequireRepositoryRoot();
        var tokens = _options.Aps.CreateTokenProvider(_httpClient, ApsScopes.All);
        _service = new WorkItemRunnerService(
            new DesignAutomationClient(_httpClient, tokens, _options.Aps.DaBaseUrl),
            new OssClient(_httpClient, tokens, _options.Aps.OssBaseUrl),
            DefinitionLoader.ForRepository(root),
            ResultReader.ForRepository(root),
            _history,
            _time);
        return _service;
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

    private string RequireRepositoryRoot() =>
        !IsRepositoryRootValid
            ? throw new InvalidOperationException("Racine du dépôt introuvable (src/Packaging/definitions) : la choisir ou définir EGR_REPO_ROOT.")
            : RepositoryRoot;

    private void Progress(string message) => AddLog("info", message);

    /// <summary>Erreurs attendues à la lecture de fichiers locaux ou d'une configuration incomplète.</summary>
    private static bool IsNonFatal(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or System.Text.Json.JsonException or System.Security.SecurityException;

    private void RaiseConfigurationChanged()
    {
        OnPropertyChanged(nameof(MissingVariables));
        OnPropertyChanged(nameof(ConfigurationBanner));
        OnPropertyChanged(nameof(HasConfigurationBanner));
    }

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

    private void RaiseCanExecuteChanged()
    {
        foreach (var command in new[] { RunCommand, StopFollowingCommand, CancelWorkItemCommand, RefreshHistoryCommand, ExportRunCommand, RecoverCommand })
        {
            command?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>IProgress synchrone : le marshalling vers l'interface est fait par <see cref="OnUi"/>.</summary>
    private sealed class Callback<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
