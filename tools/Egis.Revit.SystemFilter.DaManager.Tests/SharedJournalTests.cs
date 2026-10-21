using Egis.Revit.SystemFilter.Aps.Tests;
using Egis.Revit.SystemFilter.DaManager.Services;
using static Egis.Revit.SystemFilter.DaManager.Tests.FakeApsServer;

namespace Egis.Revit.SystemFilter.DaManager.Tests;

/// <summary>Journal partagé (dossier OneDrive) : identifiant stable par entrée, relecture et fusion avant écriture.</summary>
public sealed class SharedJournalTests
{
    private static JournalEntry Entry(string date, string alias, int bundle, int activity, string? commit = "abc1234", string revit = "2026") =>
        alias == "test"
            ? new JournalEntry(date, commit, revit, alias, new AliasVersions(bundle, null), new AliasVersions(activity, null), null)
            : new JournalEntry(date, commit, revit, alias, new AliasVersions(bundle, bundle), new AliasVersions(activity, activity), null);

    [Fact]
    public void Default_id_names_the_publication_and_is_written_to_json()
    {
        var test = Entry("2026-10-08", "test", 4, 7);
        var prod = Entry("2026-10-09", "prod", 4, 8);

        Assert.Equal("2026-10-08/R2026/test/ab4/act7", test.Id);
        Assert.Equal("2026-10-09/R2026/prod/ab4/act8", prod.Id);
        Assert.Equal("2026-10-01/R2027/test/ab-/act-", new JournalEntry("2026-10-01", null, "2027", "test", new(null, null), new(null, null), null).Id);

        var json = new PublicationJournal([test, prod]).ToJson();
        Assert.Contains("\"id\": \"2026-10-08/R2026/test/ab4/act7\"", json, StringComparison.Ordinal);
        Assert.Equal([test, prod], PublicationJournal.ParseJson(json).Entries); // relu à l'identique
    }

    [Fact]
    public void Entries_without_id_get_the_default_one()
    {
        const string legacy = """
            { "format": "egsf-publication-journal", "version": 1, "entries": [
              { "date": "2026-10-02", "commit": "9a9bbb2", "revit": "2026", "appBundle": { "test": 3, "prod": 3 }, "activity": { "test": 5, "prod": 6 }, "previousProd": null } ] }
            """;
        const string markdown = """
            | Date | Commit | Revit | Alias | AppBundle test / prod | Activity test / prod | Version précédente de l'alias (AppBundle / Activity) |
            | --- | --- | --- | --- | --- | --- | --- |
            | 2026-10-08 | cb73e3d | 2026 | test | v4 / v3 | v7 / v6 | v3 / v5 |
            """;

        Assert.Equal("2026-10-02/R2026/prod/ab3/act6", Assert.Single(PublicationJournal.ParseJson(legacy).Entries).Id);
        Assert.Equal("2026-10-08/R2026/test/ab4/act7", Assert.Single(PublicationJournal.ParseMarkdown(markdown).Entries).Id);
    }

    [Fact]
    public void Id_read_from_the_file_is_kept()
    {
        const string json = """
            { "format": "egsf-publication-journal", "version": 2, "entries": [
              { "id": "pub-42", "date": "2026-10-08", "commit": null, "revit": "2026", "alias": "test", "appBundle": { "test": 4 }, "activity": { "test": 7 }, "previous": null },
              { "id": "pub-42", "date": "2026-10-09", "commit": null, "revit": "2026", "alias": "test", "appBundle": { "test": 5 }, "activity": { "test": 8 }, "previous": null } ] }
            """;

        var journal = PublicationJournal.ParseJson(json);

        var entry = Assert.Single(journal.Entries); // même identifiant : une seule ligne, la première
        Assert.Equal("pub-42", entry.Id);
        Assert.Equal(4, entry.AppBundle.Test);
        Assert.Equal("pub-42", Assert.Single(PublicationJournal.ParseJson(journal.ToJson()).Entries).Id);
    }

    [Fact]
    public void Merge_by_id_keeps_the_existing_line()
    {
        var journal = new PublicationJournal([Entry("2026-10-08", "test", 4, 7, commit: "aaaaaaa")]);

        var merged = journal.Merge([Entry("2026-10-08", "test", 4, 7, commit: "bbbbbbb"), Entry("2026-10-09", "prod", 4, 7)]);

        Assert.Equal(2, merged.Entries.Count);
        Assert.Equal("aaaaaaa", merged.Entries[0].Commit);
    }

    [Theory]
    [InlineData("journal.json")]
    [InlineData("journal.md")]
    public void File_changed_between_load_and_save_keeps_both_entries(string name)
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, name);
        new PublicationJournal([Entry("2026-10-01", "test", 1, 1)]).Save(path);

        // Poste 1 charge le journal…
        var station1 = PublicationJournal.Load(path);

        // … poste 2 publie entre-temps et écrit le fichier partagé…
        PublicationJournal.Load(path).Add(Entry("2026-10-02", "test", 2, 2)).SaveMerged(path);

        // … puis poste 1 écrit sa propre ligne : rien n'est perdu, rien n'est doublé.
        var written = station1.Add(Entry("2026-10-03", "prod", 2, 2)).SaveMerged(path);

        var ids = PublicationJournal.Load(path).Entries.Select(e => e.Id).ToList();
        Assert.Equal(["2026-10-01/R2026/test/ab1/act1", "2026-10-02/R2026/test/ab2/act2", "2026-10-03/R2026/prod/ab2/act2"], ids);
        Assert.Equal(ids, written.Entries.Select(e => e.Id));
    }

    [Fact]
    public void Same_publication_saved_by_two_stations_is_not_duplicated()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "journal.json");
        var station1 = PublicationJournal.Load(path);
        var station2 = PublicationJournal.Load(path);

        station1.Add(Entry("2026-10-08", "test", 4, 7)).SaveMerged(path);
        station2.Add(Entry("2026-10-08", "test", 4, 7)).SaveMerged(path);
        station2.Add(Entry("2026-10-08", "test", 4, 7)).SaveMerged(path);

        Assert.Single(PublicationJournal.Load(path).Entries);
    }

    [Fact]
    public void Legacy_file_is_merged_and_upgraded()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "journal.json");
        File.WriteAllText(path, """
            { "format": "egsf-publication-journal", "version": 1, "entries": [
              { "date": "2026-10-02", "commit": null, "revit": "2026", "appBundle": { "test": 3, "prod": 3 }, "activity": { "test": 5, "prod": 6 }, "previousProd": null } ] }
            """);

        new PublicationJournal([Entry("2026-10-08", "test", 4, 7), Entry("2026-10-02", "prod", 3, 6)]).SaveMerged(path);

        var text = File.ReadAllText(path);
        Assert.Contains("\"version\": 2", text, StringComparison.Ordinal);
        Assert.Equal(2, PublicationJournal.Load(path).Entries.Count); // la ligne v1 et sa jumelle ne font qu'une
    }

    [Fact]
    public void Save_is_atomic_and_leaves_no_temporary_file()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "journal.json");

        new PublicationJournal([Entry("2026-10-08", "test", 4, 7)]).SaveMerged(path);
        new PublicationJournal([Entry("2026-10-09", "prod", 4, 7)]).SaveMerged(path);

        Assert.Equal(["journal.json"], Directory.GetFiles(temp.Path).Select(Path.GetFileName));
        Assert.Equal(2, PublicationJournal.Load(path).Entries.Count);
    }

    [Fact]
    public void Unreadable_shared_file_is_not_overwritten()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "journal.json");
        File.WriteAllText(path, "{ modifié à la main");

        Assert.Throws<InvalidOperationException>(() => new PublicationJournal([Entry("2026-10-08", "test", 4, 7)]).SaveMerged(path));

        Assert.Equal("{ modifié à la main", File.ReadAllText(path));
    }

    [Fact]
    public async Task Publication_keeps_lines_written_by_another_station_after_startup()
    {
        using var env = new TestEnvironment();
        env.Server.AppBundles["EgisSystemFilter_R2026"] = Resource.With([1, 2], ("test", 2), ("prod", 1));
        env.Server.Activities["SystemsInventory_R2026"] = Resource.With([1, 2], ("test", 2), ("prod", 1));
        env.Confirmation.Answer = true;
        Assert.Empty(env.ViewModel.Journal); // journal chargé au démarrage : vide

        // Un autre poste écrit dans le journal partagé pendant que DaManager est ouvert.
        new PublicationJournal([Entry("2026-09-30", "test", 2, 2), Entry("2026-10-01", "prod", 1, 1)]).SaveMerged(env.JournalPath);

        await env.ViewModel.PublishCommand.ExecuteAsync();

        var entries = PublicationJournal.Load(env.JournalPath).Entries;
        Assert.Equal(3, entries.Count);
        Assert.Equal(["test", "prod", "test"], entries.Select(e => e.Alias));
        Assert.Equal(3, env.ViewModel.Journal.Count); // affichage = fichier fusionné
    }

    [Fact]
    public async Task Import_into_a_file_changed_since_startup_keeps_its_lines()
    {
        using var env = new TestEnvironment();
        new PublicationJournal([Entry("2026-09-30", "test", 2, 2)]).SaveMerged(env.JournalPath);
        var source = Path.Combine(env.Root, "import.json");
        new PublicationJournal([Entry("2026-09-30", "test", 2, 2), Entry("2026-10-01", "prod", 2, 2)]).Save(source);
        env.Files.NextOpen = source;

        await env.ViewModel.ImportJournalCommand.ExecuteAsync();

        Assert.Equal(2, PublicationJournal.Load(env.JournalPath).Entries.Count);
        Assert.Contains("2 ligne(s) lue(s), 1 nouvelle(s)", env.LogText, StringComparison.Ordinal);
    }
}
