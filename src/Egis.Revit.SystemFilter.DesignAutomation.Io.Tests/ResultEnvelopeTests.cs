using System.Text.Json;
using Egis.Revit.SystemFilter.Core.Models.Envelope;
using Egis.Revit.SystemFilter.Core.Models.Inventory;
using Egis.Revit.SystemFilter.Core.Services;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

/// <summary>Calcul des pièces (RoomAnalyzer) et résultat natif (ResultEnvelopeBuilder), sans Revit.</summary>
public sealed class ResultEnvelopeTests
{
    private static BBoxRecord Box(double x0, double y0, double z0, double x1, double y1, double z1)
        => new() { Min = new[] { x0, y0, z0 }, Max = new[] { x1, y1, z1 } };

    private static PlacedRoomRecord Room(long id, string model, BBoxRecord? box, string? number = null, string? name = null)
        => new() { Id = id, RevitId = id + 100, UniqueId = "ru-" + id, ModelKey = model, Number = number, Name = name, Level = "N1", Area = 20, Height = 2700, BBox = box };

    private static NetworkElementRecord El(long id, string category, BBoxRecord? box, params long[] parents)
        => new() { Id = id, UniqueId = "eu-" + id, Category = category, Domain = NetworkDomain.Duct, Name = "El" + id, BBox = box, ParentIds = parents.ToList() };

    /// <summary>
    /// Pièces : 1 (hôte, 0..1000), 2 (lien, 1000..2000), 3 (sans système, 5000..6000), 4 (sans géométrie).
    /// Système 10 : départ 20 (CTA, pièce 1), gaine 21 (pièces 1 et 2), bouche 22 (pièce 2). Système 11 : gaine 21 partagée.
    /// </summary>
    private static SystemInventory Sample()
    {
        var inv = new SystemInventory
        {
            SchemaVersion = "1.1",
            Model = new ModelInfo { Title = "M" },
            Models = new List<ModelRecord>
            {
                new() { Key = "main", Title = "M", IsHost = true },
                new() { Key = "link-7", Title = "Archi", IsHost = false },
            },
            Rooms = new List<PlacedRoomRecord>
            {
                Room(1, "main", Box(0, 0, 0, 1000, 1000, 3000), "101", "Salle"),
                Room(2, "link-7", Box(1000, 0, 0, 2000, 1000, 3000), "102", "Bureau"),
                Room(3, "link-7", Box(5000, 0, 0, 6000, 1000, 3000)),
                Room(4, "main", null),
            },
            SystemTypes = new List<SystemTypeRecord> { new() { Id = 1, Name = "Soufflage", Domain = NetworkDomain.Duct, Classification = "SupplyAir" } },
            Systems = new List<SystemRecord>
            {
                new() { Id = 10, UniqueId = "su-10", Name = "S10", SystemTypeId = 1, BaseEquipmentId = 20, Levels = new List<string> { "N0", "N1" } },
                new() { Id = 11, UniqueId = "su-11", Name = "S11", SystemTypeId = 1 },
            },
            Elements = new List<NetworkElementRecord>
            {
                El(20, "MechanicalEquipment", Box(100, 100, 100, 500, 500, 500), 10),
                El(21, "DuctCurves", Box(400, 100, 100, 1500, 300, 300), 10, 11),
                El(22, "OST_DuctTerminal", Box(1200, 100, 100, 1400, 300, 300), 10),
                El(23, "DuctFitting", null, 11),
            },
        };
        return inv;
    }

    private static EnvelopeObject Find(IEnumerable<EnvelopeObject> list, string id)
    {
        foreach (var o in list)
        {
            if (o.Id == id) return o;
            var inner = FindOrNull(o.Children, id);
            if (inner != null) return inner;
        }
        throw new Xunit.Sdk.XunitException("objet introuvable : " + id);
    }

    private static EnvelopeObject? FindOrNull(IEnumerable<EnvelopeObject> list, string id)
    {
        foreach (var o in list)
        {
            if (o.Id == id) return o;
            var inner = FindOrNull(o.Children, id);
            if (inner != null) return inner;
        }
        return null;
    }

    private static string[] Refs(EnvelopeObject o, string key)
        => o.Properties.Where(p => p.Key == key && p.Type == "ref").Select(p => (string)p.Value!).ToArray();

    private static object? Prop(EnvelopeObject o, string key) => o.Properties.FirstOrDefault(p => p.Key == key)?.Value;

    // ---------- RoomAnalyzer ----------

    [Fact]
    public void Pieces_traversees_desservies_et_de_depart()
    {
        var a = RoomAnalyzer.Analyze(Sample());
        Assert.Equal(new long[] { 1 }, a.ElementRoomIds[20]);
        Assert.Equal(new long[] { 1, 2 }, a.ElementRoomIds[21]);
        Assert.Equal(new long[] { 1, 2 }, a.Systems[10].Crossed);
        Assert.Equal(new long[] { 2 }, a.Systems[10].Served);
        Assert.Equal(new long[] { 1 }, a.Systems[10].Start);
        Assert.Equal(new long[] { 1, 2 }, a.Systems[11].Crossed);
        Assert.Empty(a.Systems[11].Served);
        Assert.Empty(a.Systems[11].Start);
        Assert.Equal(3, a.RoomsWithGeometry);
        Assert.Equal(3, a.AnalyzedElements);
        Assert.Equal(3, a.ElementsInRooms);
        Assert.False(a.ElementRoomIds.ContainsKey(23), "sans boîte : jamais analysé");
    }

    [Theory]
    [InlineData(1.0, false)]   // recouvrement de 1 mm exactement : pas un conflit (strictement supérieur)
    [InlineData(1.1, true)]
    [InlineData(0.0, false)]   // simple contact : jamais un conflit
    public void Tolerance_de_1_mm_sur_chaque_axe(double overlapMm, bool conflict)
    {
        var inv = new SystemInventory
        {
            Rooms = new List<PlacedRoomRecord> { Room(1, "main", Box(0, 0, 0, 1000, 1000, 1000)) },
            Systems = new List<SystemRecord> { new() { Id = 10, SystemTypeId = 1 } },
            Elements = new List<NetworkElementRecord> { El(1, "DuctCurves", Box(1000 - overlapMm, 0, 0, 2000, 1000, 1000), 10) },
        };
        var a = RoomAnalyzer.Analyze(inv);
        Assert.Equal(conflict, a.ElementRoomIds[1].Count == 1);
    }

    [Fact]
    public void Un_axe_sans_recouvrement_suffit_a_exclure()
    {
        var inv = new SystemInventory
        {
            Rooms = new List<PlacedRoomRecord> { Room(1, "main", Box(0, 0, 0, 1000, 1000, 1000)) },
            Systems = new List<SystemRecord> { new() { Id = 10, SystemTypeId = 1 } },
            Elements = new List<NetworkElementRecord> { El(1, "DuctCurves", Box(100, 100, 1000, 900, 900, 2000), 10) },
        };
        Assert.Empty(RoomAnalyzer.Analyze(inv).ElementRoomIds[1]);
    }

    [Fact]
    public void Inventaire_sans_pieces_donne_une_analyse_vide()
    {
        var inv = Sample();
        inv.Rooms = null;
        var a = RoomAnalyzer.Analyze(inv);
        Assert.Equal(0, a.RoomsWithGeometry);
        Assert.All(a.Systems.Values, s => Assert.Empty(s.Crossed));
    }

    // ---------- ResultEnvelopeBuilder ----------

    [Fact]
    public void Structure_systemes_elements_pieces()
    {
        var inv = Sample();
        var env = ResultEnvelopeBuilder.Build(inv, RoomAnalyzer.Analyze(inv));
        Assert.Equal("1.0", env.Version);
        Assert.Equal("done", env.Status);
        Assert.Empty(env.Warnings);
        Assert.Equal(new[] { "main", "link-7" }, env.Models.Select(m => m.Key));
        // 2 systèmes + la pièce 3 (sans système) ; la pièce 4 (sans géométrie, sans système) est aussi une racine.
        Assert.Equal(new[] { "system-10", "system-11", "room-3", "room-4" }, env.Objects.Select(o => o.Id));
        var s10 = env.Objects[0];
        Assert.Equal(new[] { "element-20", "element-21", "element-22", "room-1", "room-2" }, s10.Children.Select(c => c.Id));
        Assert.Equal("S10", s10.Name);
        Assert.Equal("Soufflage", Prop(s10, "systemType"));
        Assert.Equal("N0, N1", Prop(s10, "levels"));
        Assert.Equal("main", Prop(s10, "modelKey"));
        Assert.Equal(new[] { "element-20" }, Refs(s10, "baseEquipment"));
        Assert.Equal("su-10", Prop(s10, "viewerId"));
    }

    [Fact]
    public void Role_par_systeme_et_type_global_de_la_piece()
    {
        var inv = Sample();
        var env = ResultEnvelopeBuilder.Build(inv, RoomAnalyzer.Analyze(inv));
        var r1 = Find(env.Objects, "room-1");
        var r2 = Find(env.Objects, "room-2");
        Assert.Equal("départ", Prop(r1, "type"));
        Assert.Equal(new[] { "system-10", "system-11" }, Refs(r1, "system"));
        Assert.Equal(new[] { "system-10" }, Refs(r1, "startOf"));
        Assert.Empty(Refs(r1, "servedBy"));
        Assert.Equal("desservie", Prop(r2, "type"));
        Assert.Equal(new[] { "system-10" }, Refs(r2, "servedBy"));
        Assert.Equal(new[] { "system-10", "system-11" }, Refs(r2, "system"));
        Assert.Equal("link-7", Prop(r2, "modelKey"));
        Assert.Equal("101 Salle", r1.Name);
    }

    [Fact]
    public void Pieces_sans_systeme_sont_des_racines_sans_type()
    {
        var inv = Sample();
        var env = ResultEnvelopeBuilder.Build(inv, RoomAnalyzer.Analyze(inv));
        var loose = env.Objects.Where(o => o.Id.StartsWith("room-")).ToList();
        Assert.Equal(2, loose.Count);
        Assert.All(loose, r =>
        {
            Assert.Null(Prop(r, "type"));
            Assert.Empty(Refs(r, "system"));
        });
        Assert.Equal("Pièce 4", loose[1].Name);
        Assert.Null(Prop(loose[1], "bbox"));
        Assert.NotNull(Prop(loose[0], "bbox"));
    }

    [Fact]
    public void Elements_portent_leurs_pieces_et_la_maquette_hote_et_le_partage_est_identique()
    {
        var inv = Sample();
        var env = ResultEnvelopeBuilder.Build(inv, RoomAnalyzer.Analyze(inv));
        var e21a = Find(env.Objects[0].Children, "element-21");
        var e21b = Find(env.Objects[1].Children, "element-21");
        Assert.Equal(new[] { "room-1", "room-2" }, Refs(e21a, "room"));
        Assert.Equal("main", Prop(e21a, "modelKey"));
        Assert.Equal(JsonSerializer.Serialize(e21a), JsonSerializer.Serialize(e21b));
        Assert.Empty(Refs(Find(env.Objects[1].Children, "element-23"), "room"));
    }

    [Fact]
    public void Aucune_reference_ne_pointe_dans_le_vide_et_les_ids_sont_coherents()
    {
        var inv = Sample();
        var env = ResultEnvelopeBuilder.Build(inv, RoomAnalyzer.Analyze(inv));
        var ids = new Dictionary<string, string>();
        var refs = new List<string>();
        void Walk(EnvelopeObject o)
        {
            var shape = JsonSerializer.Serialize(o);
            if (ids.TryGetValue(o.Id, out var known)) { Assert.Equal(known, shape); return; }
            ids[o.Id] = shape;
            var keys = new HashSet<string>();
            foreach (var p in o.Properties)
            {
                if (p.Type == "ref") refs.Add((string)p.Value!);
                else Assert.True(keys.Add(p.Key), "clé en double : " + p.Key);
            }
            foreach (var c in o.Children) Walk(c);
        }
        foreach (var o in env.Objects) Walk(o);
        Assert.All(refs, r => Assert.True(ids.ContainsKey(r), "référence non résolue : " + r));
        // Les 4 pièces de l'inventaire sont toutes dans l'enveloppe.
        Assert.Equal(4, ids.Keys.Count(k => k.StartsWith("room-")));
    }

    [Fact]
    public void Serialisation_json_champs_attendus_et_unites()
    {
        var inv = Sample();
        var json = JsonSerializer.Serialize(ResultEnvelopeBuilder.Build(inv, RoomAnalyzer.Analyze(inv)));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("1.0", root.GetProperty("contractVersion").GetString());
        Assert.False(root.TryGetProperty("serviceId", out _));
        var room = root.GetProperty("objects")[0].GetProperty("children").EnumerateArray().First(c => c.GetProperty("id").GetString() == "room-1");
        var height = room.GetProperty("properties").EnumerateArray().First(p => p.GetProperty("key").GetString() == "height");
        Assert.Equal("number", height.GetProperty("type").GetString());
        Assert.Equal("mm", height.GetProperty("unit").GetString());
        Assert.Equal(2700, height.GetProperty("value").GetDouble());
        var box = room.GetProperty("properties").EnumerateArray().First(p => p.GetProperty("key").GetString() == "bbox");
        Assert.Equal("box", box.GetProperty("type").GetString());
        Assert.Equal(1000, box.GetProperty("value").GetProperty("max")[0].GetDouble());
    }

    [Fact]
    public void Proprietes_des_elements_ont_un_libelle_lisible_et_la_longueur_en_mm()
    {
        var inv = Sample();
        var full = El(24, "DuctCurves", Box(0, 0, 0, 10, 10, 10), 10);
        full.Family = "Gaine rectangulaire"; full.Type = "Standard"; full.Level = "N1"; full.Size = "300x200";
        full.LengthMm = 1500; full.Materiel = "Acier"; full.NumCta = "CTA-1";
        inv.Elements.Add(full);
        var env = ResultEnvelopeBuilder.Build(inv, RoomAnalyzer.Analyze(inv));
        var system = env.Objects.First(o => o.Id == "system-10");
        var element = Find(system.Children, "element-24");
        EnvelopeProperty P(EnvelopeObject o, string key) => o.Properties.First(p => p.Key == key);

        // Aucun libellé n'est la clé brute : tableau d'éléments seuls lisible.
        foreach (var p in element.Properties.Where(p => p.Key != "room"))
            Assert.NotEqual(p.Key, p.Label);
        Assert.Equal("Catégorie", P(element, "category").Label);
        Assert.Equal("Domaine", P(element, "domain").Label);
        Assert.Equal("Famille", P(element, "family").Label);
        Assert.Equal("Type", P(element, "type").Label);
        Assert.Equal("Niveau", P(element, "level").Label);
        Assert.Equal("Taille", P(element, "size").Label);
        Assert.Equal("Matériel", P(element, "materiel").Label);
        Assert.Equal("N° de CTA", P(element, "numCta").Label);
        Assert.Equal("Longueur", P(element, "lengthMm").Label);
        Assert.Equal("mm", P(element, "lengthMm").Unit);
        Assert.Equal(1500d, P(element, "lengthMm").Value);

        // Même texte que sur les pièces et les systèmes pour une même clé.
        var room = Find(system.Children, "room-1");
        Assert.Equal(P(room, "level").Label, P(element, "level").Label);
        Assert.Equal(P(system, "domain").Label, P(element, "domain").Label);
        Assert.Equal("Identifiant du type de système", P(system, "systemTypeId").Label);
    }

    [Fact]
    public void Inventaire_sans_pieces_donne_partial_et_un_avertissement()
    {
        var inv = Sample();
        inv.Rooms = null;
        inv.Models = null;
        var env = ResultEnvelopeBuilder.Build(inv, RoomAnalyzer.Analyze(inv));
        Assert.Equal("partial", env.Status);
        Assert.Single(env.Warnings);
        Assert.Equal(new[] { "system-10", "system-11" }, env.Objects.Select(o => o.Id));
        Assert.Equal("main", Assert.Single(env.Models).Key);
        Assert.Equal("main", Prop(env.Objects[0], "modelKey"));
        Assert.DoesNotContain(env.Objects[0].Children, c => c.Id.StartsWith("room-"));
    }

    [Fact]
    public void Ecriture_dans_result_refuse_un_nom_avec_dossier()
    {
        using var dir = new TempDir();
        var ws = new JobWorkspace(dir.Path);
        Assert.Throws<ArgumentException>(() => ws.WriteResultFile("../x.json", new byte[] { 1 }));
        var written = ws.WriteResultFile(ResultEnvelope.FileName, new byte[] { 1 });
        Assert.Equal(ResultEnvelope.FileName, Path.GetFileName(written));
        Assert.Equal(ws.ResultDirectory, Path.GetDirectoryName(written));
    }
}
