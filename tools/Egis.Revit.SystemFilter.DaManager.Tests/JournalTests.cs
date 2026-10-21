using Egis.Revit.SystemFilter.Aps.DesignAutomation;
using Egis.Revit.SystemFilter.Aps.Tests;
using Egis.Revit.SystemFilter.DaManager.Services;
using static Egis.Revit.SystemFilter.DaManager.Tests.FakeApsServer;

namespace Egis.Revit.SystemFilter.DaManager.Tests;

public sealed class JournalTests
{
    /// <summary>Journal au format 1 (promotions seulement), tel qu'écrit avant le journal des publications en test.</summary>
    private const string Json = """
        {
          "format": "egsf-publication-journal",
          "version": 1,
          "entries": [
            {
              "date": "2026-10-02",
              "commit": "9a9bbb2",
              "revit": "2026",
              "appBundle": { "test": 3, "prod": 3 },
              "activity": { "test": 5, "prod": 6 },
              "previousProd": { "appBundle": 2, "activity": 4 }
            },
            {
              "date": "2026-09-20",
              "commit": null,
              "revit": "2027",
              "appBundle": { "test": 1, "prod": 1 },
              "activity": { "test": 1, "prod": 2 },
              "previousProd": null
            }
          ]
        }
        """;

    /// <summary>Ancienne table à 6 colonnes (sans « Alias »).</summary>
    private const string Markdown = """
        Publication du 2 octobre :

        | Date | Commit | Revit | AppBundle test / prod | Activity test / prod | Prod précédente (AppBundle / Activity) |
        | --- | --- | --- | --- | --- | --- |
        | 2026-10-02 | `9a9bbb2` | 2026 | v3 / v3 | 5 / 6 | v2 / v4 |
        | 2026-09-20 | — | 2027 | v1 / v1 | v1 / v2 | — |
        """;

    /// <summary>Format courant : publications en test et promotions.</summary>
    private const string JsonV2 = """
        {
          "format": "egsf-publication-journal",
          "version": 2,
          "entries": [
            {
              "date": "2026-10-08",
              "commit": "cb73e3d",
              "revit": "2026",
              "alias": "test",
              "appBundle": { "test": 4, "prod": 3 },
              "activity": { "test": 7, "prod": 6 },
              "previous": { "appBundle": 3, "activity": 5 }
            },
            {
              "date": "2026-10-09",
              "commit": "9b9a3fb",
              "revit": "2026",
              "alias": "prod",
              "appBundle": { "test": 4, "prod": 4 },
              "activity": { "test": 7, "prod": 8 },
              "previous": { "appBundle": 3, "activity": 6 }
            }
          ]
        }
        """;

    private const string MarkdownV2 = """
        | Date | Commit | Revit | Alias | AppBundle test / prod | Activity test / prod | Version précédente de l'alias (AppBundle / Activity) |
        | --- | --- | --- | --- | --- | --- | --- |
        | 2026-10-08 | cb73e3d | 2026 | test | v4 / v3 | v7 / v6 | v3 / v5 |
        | 2026-10-09 | 9b9a3fb | 2026 | prod | v4 / v4 | v7 / v8 | v3 / v6 |
        """;

    [Fact]
    public void Legacy_journal_still_loads_as_promotions()
    {
        var fromJson = PublicationJournal.ParseJson(Json);
        var fromMarkdown = PublicationJournal.ParseMarkdown(Markdown);

        Assert.All(fromJson.Entries, e => Assert.Equal("prod", e.Alias));
        Assert.Equal(fromJson.Entries, fromMarkdown.Entries);
        Assert.Equal(new PreviousVersions(2, 4), fromJson.Entries[1].Previous);

        // Réécrit au format courant, puis relu à l'identique.
        Assert.Contains("\"version\": 2", fromJson.ToJson(), StringComparison.Ordinal);
        Assert.Equal(fromJson.Entries, PublicationJournal.ParseJson(fromJson.ToJson()).Entries);
        Assert.Equal(fromJson.Entries, PublicationJournal.ParseMarkdown(fromJson.ToMarkdown()).Entries);
    }

    [Fact]
    public void Current_format_records_test_and_prod_publications()
    {
        var journal = PublicationJournal.ParseJson(JsonV2);

        Assert.Equal(["test", "prod"], journal.Entries.Select(e => e.Alias));
        Assert.Equal(journal.Entries, PublicationJournal.ParseMarkdown(MarkdownV2).Entries);
        Assert.Equal(MarkdownV2.Replace("\r", string.Empty, StringComparison.Ordinal).Trim(), journal.ToMarkdown().Trim());
        Assert.Contains("\"alias\": \"test\"", journal.ToJson(), StringComparison.Ordinal);
        Assert.Contains("\"previous\": {", journal.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("previousProd", journal.ToJson(), StringComparison.Ordinal);
        Assert.Equal([3, 4], journal.ProtectedVersions("2026", DaResourceKind.AppBundle));
        Assert.Equal([5, 6, 7, 8], journal.ProtectedVersions("2026", DaResourceKind.Activity));
    }

    [Fact]
    public void Legacy_and_current_entries_merge()
    {
        var merged = PublicationJournal.ParseMarkdown(Markdown).Merge(PublicationJournal.ParseMarkdown(MarkdownV2).Entries);

        Assert.Equal(4, merged.Entries.Count);
        Assert.Equal(merged.Entries, PublicationJournal.ParseJson(merged.ToJson()).Entries);
    }

    [Fact]
    public void Json_and_markdown_describe_the_same_journal()
    {
        var fromJson = PublicationJournal.ParseJson(Json);
        var fromMarkdown = PublicationJournal.ParseMarkdown(Markdown);

        Assert.Equal(fromJson.Entries, fromMarkdown.Entries);
        Assert.Equal(["2027", "2026"], fromJson.Entries.Select(e => e.Revit)); // triées par date
        var entry = fromJson.Entries[1];
        Assert.Equal(new AliasVersions(5, 6), entry.Activity);
        Assert.Equal(new PreviousVersions(2, 4), entry.Previous);
        Assert.Null(fromJson.Entries[0].Commit);
        Assert.Null(fromJson.Entries[0].Previous);
    }

    [Fact]
    public void Export_is_readable_and_round_trips()
    {
        var journal = PublicationJournal.ParseJson(Json);

        var json = journal.ToJson();
        var markdown = journal.ToMarkdown();

        Assert.Contains("  \"entries\": [", json, StringComparison.Ordinal); // indenté (CRLF ou LF)
        Assert.Contains("\"date\": \"2026-10-02\"", json, StringComparison.Ordinal);
        Assert.Equal(journal.Entries, PublicationJournal.ParseJson(json).Entries);
        Assert.StartsWith("| Date | Commit | Revit | Alias | AppBundle test / prod | Activity test / prod | Version précédente de l'alias (AppBundle / Activity) |\n| --- |", markdown, StringComparison.Ordinal);
        Assert.Contains("| 2026-10-02 | 9a9bbb2 | 2026 | prod | v3 / v3 | v5 / v6 | v2 / v4 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| 2026-09-20 | — | 2027 | prod | v1 / v1 | v1 / v2 | — |", markdown, StringComparison.Ordinal);
        Assert.Equal(journal.Entries, PublicationJournal.ParseMarkdown(markdown).Entries);
    }

    [Fact]
    public void Save_and_load_follow_the_extension()
    {
        using var temp = new TempDirectory();
        var journal = PublicationJournal.ParseJson(Json);
        var json = Path.Combine(temp.Path, "sub", "journal.json");
        var markdown = Path.Combine(temp.Path, "journal.md");

        journal.Save(json);
        journal.Save(markdown);

        Assert.StartsWith("{", File.ReadAllText(json), StringComparison.Ordinal);
        Assert.StartsWith("| Date", File.ReadAllText(markdown), StringComparison.Ordinal);
        Assert.Equal(journal.Entries, PublicationJournal.Load(json).Entries);
        Assert.Equal(journal.Entries, PublicationJournal.Load(markdown).Entries);
        Assert.Empty(PublicationJournal.Load(Path.Combine(temp.Path, "absent.json")).Entries);
    }

    [Fact]
    public void Protected_versions_are_per_revit_version_and_kind()
    {
        var journal = PublicationJournal.ParseJson(Json);

        Assert.Equal([2, 3], journal.ProtectedVersions("2026", DaResourceKind.AppBundle));
        Assert.Equal([4, 5, 6], journal.ProtectedVersions("2026", DaResourceKind.Activity));
        Assert.Equal([1, 2], journal.ProtectedVersions("2027", DaResourceKind.Activity));
        Assert.Empty(journal.ProtectedVersions("2099", DaResourceKind.Activity));
    }

    [Fact]
    public void Merge_ignores_duplicates()
    {
        var journal = PublicationJournal.ParseJson(Json);

        var merged = journal.Merge(PublicationJournal.ParseMarkdown(Markdown).Entries);

        Assert.Equal(2, merged.Entries.Count);
    }

    [Theory]
    [InlineData("{ pas du json", "illisible")]
    [InlineData("""{ "format": "autre", "version": 1, "entries": [] }""", "format")]
    [InlineData("""{ "format": "egsf-publication-journal", "version": 3, "entries": [] }""", "non prise en charge")]
    [InlineData("""{ "format": "egsf-publication-journal", "version": 2, "entries": [ { "date": "2026-10-02", "revit": "2026", "appBundle": {}, "activity": {} } ] }""", "« alias » manquant")]
    [InlineData("""{ "format": "egsf-publication-journal", "version": 2, "entries": [ { "date": "2026-10-02", "revit": "2026", "alias": "beta", "appBundle": {}, "activity": {} } ] }""", "alias « beta »")]
    [InlineData("""{ "format": "egsf-publication-journal", "version": 1, "entries": [ { "date": "02/10/2026", "revit": "2026", "appBundle": {}, "activity": {} } ] }""", "entrée 1 : date")]
    [InlineData("""{ "format": "egsf-publication-journal", "version": 1, "entries": [ { "date": "2026-10-02", "revit": "2019", "appBundle": {}, "activity": {} } ] }""", "Revit « 2019 »")]
    [InlineData("""{ "format": "egsf-publication-journal", "version": 1, "entries": [ { "date": "2026-10-02", "revit": "2026", "appBundle": { "test": 0 }, "activity": {} } ] }""", "version 0")]
    [InlineData("""{ "format": "egsf-publication-journal", "version": 1, "entries": [ { "date": "2026-10-02", "revit": "2026", "appBundle": { "test": "3" }, "activity": {} } ] }""", "entier attendu")]
    public void Invalid_json_is_rejected_with_a_clear_message(string json, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PublicationJournal.ParseJson(json));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("| 2026-10-02 | abc | 2026 | v3 | v5 / v6 | — |", "ligne 1 : « v3 »")]
    [InlineData("| 2026-10-02 | abc | 2026 | v3 / x | v5 / v6 | — |", "version « x »")]
    [InlineData("| 2026-10-02 | abc | 2026 | v3 / v3 |", "7 colonnes")]
    [InlineData("| 2026-10-02 | abc | 2026 | beta | v3 / v3 | v5 / v6 | — |", "alias « beta »")]
    public void Invalid_markdown_is_rejected_with_the_line(string markdown, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PublicationJournal.ParseMarkdown(markdown));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_read_the_journal_path()
    {
        var options = DaManagerOptions.FromEnvironment(new Dictionary<string, string?> { ["EGSF_PUBLICATION_JOURNAL"] = "/data/journal.md", ["EGSF_REPO_ROOT"] = "/r" });
        var fallback = DaManagerOptions.FromEnvironment(new Dictionary<string, string?> { ["EGSF_REPO_ROOT"] = "/r" });

        Assert.Equal("/data/journal.md", options.JournalPath);
        Assert.Equal(DaManagerOptions.DefaultJournalPath(), fallback.JournalPath);
        Assert.EndsWith("publication-journal.json", fallback.JournalPath, StringComparison.Ordinal);
    }

    // ---- ViewModel -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cleanup_automatically_protects_journal_versions()
    {
        using var env = new TestEnvironment();
        PublicationJournal.ParseJson(Json).Save(env.JournalPath);
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With(Enumerable.Range(1, 12), ("prod", 12), ("test", 12));
        await env.ViewModel.RefreshCommand.ExecuteAsync();
        env.ViewModel.SelectedResource = env.ViewModel.Resources.Single(r => r.Id == "EgisSystemFilter_R2026");
        env.ViewModel.KeepLatest = 1;
        env.ViewModel.ProtectedVersions = "7";
        env.Confirmation.Answer = true;

        await env.ViewModel.CleanupCommand.ExecuteAsync();

        var plan = env.Confirmation.Plans.Single().Text;
        Assert.Contains("conserver v2 (protégée (journal))", plan, StringComparison.Ordinal);
        Assert.Contains("conserver v3 (protégée (journal))", plan, StringComparison.Ordinal);
        Assert.Contains("Journal de publication : v2, v3 protégée(s) automatiquement.", plan, StringComparison.Ordinal);
        Assert.Equal([2, 3, 7, 12], env.Server.AppBundles["EgisSystemFilter_R2026"].Versions);
    }

    [Fact]
    public async Task Unreadable_journal_blocks_cleanup()
    {
        using var env = new TestEnvironment();
        Directory.CreateDirectory(Path.GetDirectoryName(env.JournalPath)!);
        File.WriteAllText(env.JournalPath, "{ cassé");
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With(Enumerable.Range(1, 12), ("test", 12));
        await env.ViewModel.RefreshCommand.ExecuteAsync();
        env.ViewModel.SelectedResource = env.ViewModel.Resources.Single(r => r.Id == "EgisSystemFilter_R2026");
        env.ViewModel.KeepLatest = 1;
        env.Confirmation.Answer = true;

        await env.ViewModel.CleanupCommand.ExecuteAsync();

        Assert.Empty(env.Confirmation.Plans);
        Assert.Empty(env.Server.Writes);
        Assert.Contains("Journal JSON illisible", env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Promotion_is_recorded_in_the_journal()
    {
        using var env = new TestEnvironment();
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With([1, 2, 3], ("test", 3), ("prod", 2));
        env.Server.Activities["SystemsInventory_R2026"] = Resource.With([1, 2, 3], ("test", 3), ("prod", 2));
        env.Confirmation.Answer = true;

        await env.ViewModel.PromoteCommand.ExecuteAsync();

        var entry = Assert.Single(PublicationJournal.Load(env.JournalPath).Entries);
        Assert.Equal("2026", entry.Revit);
        Assert.Equal(new AliasVersions(3, 3), entry.AppBundle);
        Assert.Equal(new AliasVersions(3, 4), entry.Activity);
        Assert.Equal("prod", entry.Alias);
        Assert.Equal(new PreviousVersions(2, 2), entry.Previous);
        Assert.Equal(DateTimeOffset.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), entry.Date);
        Assert.Equal(entry, Assert.Single(env.ViewModel.Journal));
        Assert.Contains("Journal de publication : ", env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_publication_is_recorded_with_its_alias()
    {
        using var env = new TestEnvironment();
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With([1, 2], ("test", 2), ("prod", 1));
        env.Server.Activities["SystemsInventory_R2026"] = Resource.With([1, 2], ("test", 2), ("prod", 1));
        env.Confirmation.Answer = true;

        await env.ViewModel.PublishCommand.ExecuteAsync();

        var entry = Assert.Single(PublicationJournal.Load(env.JournalPath).Entries);
        Assert.Equal("test", entry.Alias);
        Assert.Equal(new AliasVersions(3, 1), entry.AppBundle);
        Assert.Equal(new AliasVersions(3, 1), entry.Activity);
        Assert.Equal(new PreviousVersions(2, 2), entry.Previous);
        Assert.Contains("· test ·", env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task First_test_publication_has_no_previous_version_and_is_protected()
    {
        using var env = new TestEnvironment();
        env.Confirmation.Answer = true;

        await env.ViewModel.PublishCommand.ExecuteAsync();
        await env.ViewModel.PromoteCommand.ExecuteAsync();

        var entries = PublicationJournal.Load(env.JournalPath).Entries;
        Assert.Equal(["prod", "test"], entries.Select(e => e.Alias).Order(StringComparer.Ordinal));
        var test = entries.Single(e => e.Alias == "test");
        Assert.Null(test.Previous);
        Assert.Equal(new AliasVersions(1, null), test.AppBundle);
        Assert.Equal([1], PublicationJournal.Load(env.JournalPath).ProtectedVersions("2026", DaResourceKind.AppBundle));
    }

    [Fact]
    public async Task Legacy_journal_file_is_upgraded_on_next_publication()
    {
        using var env = new TestEnvironment();
        Directory.CreateDirectory(Path.GetDirectoryName(env.JournalPath)!);
        File.WriteAllText(env.JournalPath, Json);
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With([1, 2, 3], ("test", 3), ("prod", 3));
        env.Server.Activities["SystemsInventory_R2026"] = Resource.With([1, 2, 3, 4, 5, 6], ("test", 5), ("prod", 6));
        env.Confirmation.Answer = true;

        await env.ViewModel.PublishCommand.ExecuteAsync();

        var text = File.ReadAllText(env.JournalPath);
        Assert.Contains("\"version\": 2", text, StringComparison.Ordinal);
        var journal = PublicationJournal.Load(env.JournalPath);
        Assert.Equal(3, journal.Entries.Count);
        Assert.Equal(2, journal.Entries.Count(e => e.Alias == "prod"));
        Assert.Equal(3, env.ViewModel.Journal.Count);
    }

    [Fact]
    public async Task Declined_promotion_records_nothing()
    {
        using var env = new TestEnvironment();
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With([1, 2, 3], ("test", 3), ("prod", 2));
        env.Server.Activities["SystemsInventory_R2026"] = Resource.With([1, 2, 3], ("test", 3), ("prod", 2));

        await env.ViewModel.PromoteCommand.ExecuteAsync();

        Assert.False(File.Exists(env.JournalPath));
        Assert.Empty(env.ViewModel.Journal);
    }

    [Fact]
    public async Task Import_merges_markdown_and_export_writes_json()
    {
        using var env = new TestEnvironment();
        var source = Path.Combine(env.Root, "pr-description.md");
        File.WriteAllText(source, Markdown);
        env.Files.NextOpen = source;

        await env.ViewModel.ImportJournalCommand.ExecuteAsync();
        await env.ViewModel.ImportJournalCommand.ExecuteAsync(); // second import : aucun doublon

        Assert.Equal(2, PublicationJournal.Load(env.JournalPath).Entries.Count);
        Assert.Equal(2, env.ViewModel.Journal.Count);
        Assert.Equal("2026", env.ViewModel.Journal[0].Revit); // plus récente en tête
        Assert.Contains("2 ligne(s) lue(s), 2 nouvelle(s)", env.LogText, StringComparison.Ordinal);
        Assert.Contains("2 ligne(s) lue(s), 0 nouvelle(s)", env.LogText, StringComparison.Ordinal);

        env.Files.NextSave = Path.Combine(env.Root, "export.json");
        await env.ViewModel.ExportJournalCommand.ExecuteAsync();

        Assert.Equal(PublicationJournal.ParseMarkdown(Markdown).Entries, PublicationJournal.ParseJson(File.ReadAllText(env.Files.NextSave)).Entries);
        Assert.Empty(env.Server.Requests);
    }

    [Fact]
    public async Task Invalid_import_leaves_the_journal_untouched()
    {
        using var env = new TestEnvironment();
        PublicationJournal.ParseJson(Json).Save(env.JournalPath);
        var before = File.ReadAllText(env.JournalPath);
        var source = Path.Combine(env.Root, "bad.md");
        File.WriteAllText(source, "| 2026-10-02 | abc | 2026 | v3 / x | v5 / v6 | — |");
        env.Files.NextOpen = source;

        await env.ViewModel.ImportJournalCommand.ExecuteAsync();

        Assert.Equal(before, File.ReadAllText(env.JournalPath));
        Assert.Contains("Journal Markdown, ligne 1", env.LogText, StringComparison.Ordinal);
    }
}
