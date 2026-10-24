using System.Text.Json;
using Egis.Revit.SystemFilter.Core.Models.Envelope;
using Egis.Revit.SystemFilter.Core.Models.Inventory;
using Egis.Revit.SystemFilter.Core.Services;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

/// <summary>
/// Avertissements du plugin repris dans <c>warnings[]</c> de l'enveloppe, et identifiant Revit des éléments. Sans Revit.
/// Le backend (<c>validateEnvelope</c>) n'impose que « warnings = tableau de textes » ; les limites testées ici sont celles du plugin.
/// </summary>
public sealed class EnvelopeWarningsAndRevitIdTests
{
    private static SystemInventory Sample()
    {
        var box = new BBoxRecord { Min = new double[] { 0, 0, 0 }, Max = new double[] { 10, 10, 10 } };
        return new SystemInventory
        {
            SchemaVersion = "1.1",
            Model = new ModelInfo { Title = "M" },
            Models = new List<ModelRecord> { new() { Key = "main", Title = "M", IsHost = true } },
            Rooms = new List<PlacedRoomRecord>(),
            SystemTypes = new List<SystemTypeRecord> { new() { Id = 1, Name = "Soufflage", Domain = NetworkDomain.Duct } },
            Systems = new List<SystemRecord>
            {
                new() { Id = 10, UniqueId = "su-10", Name = "S10", SystemTypeId = 1 },
                new() { Id = 11, UniqueId = "su-11", Name = "S11", SystemTypeId = 1 },
            },
            Elements = new List<NetworkElementRecord>
            {
                new() { Id = 2001, UniqueId = "eu-2001", Category = "DuctCurves", Domain = NetworkDomain.Duct, Name = "Gaine", BBox = box, ParentIds = new List<long> { 10, 11 } },
                new() { Id = 2002, UniqueId = "eu-2002", Category = "DuctFitting", Domain = NetworkDomain.Duct, ParentIds = new List<long> { 10 } },
                new() { Id = 2003, UniqueId = "", Category = "DuctTerminal", Domain = NetworkDomain.Duct, ParentIds = new List<long> { 11 } },
            },
        };
    }

    /// <summary>Mêmes options que SystemInventoryExport.SerializeEnvelope (null omis, pas d'échappement de &lt; ni des accents).</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Json(ResultEnvelope env) => JsonSerializer.Serialize(env, JsonOptions);

    private static ResultEnvelope Build(IEnumerable<string>? warnings, SystemInventory? inv = null)
    {
        inv ??= Sample();
        return ResultEnvelopeBuilder.Build(inv, RoomAnalyzer.Analyze(inv), warnings);
    }

    private static IEnumerable<EnvelopeObject> Elements(ResultEnvelope env)
        => env.Objects.SelectMany(s => s.Children).Where(c => c.Id.StartsWith("element-"));

    private static string? Text(EnvelopeObject o, string key) => (string?)o.Properties.FirstOrDefault(p => p.Key == key)?.Value;

    // ---------- warnings[] ----------

    [Fact]
    public void Les_avertissements_du_plugin_arrivent_dans_l_enveloppe_et_la_rendent_partial()
    {
        var log = new RunLog();
        log.Info("pas un avertissement");
        log.Warn("lien manquant : ARC.rvt (absent du ZIP, non chargé)");
        log.Warn("2 équipement(s) sans CTA");

        var env = Build(log.WarningMessages);

        Assert.Equal(new[] { "lien manquant : ARC.rvt (absent du ZIP, non chargé)", "2 équipement(s) sans CTA" }, env.Warnings);
        Assert.Equal("partial", env.Status);
    }

    [Fact]
    public void Sans_avertissement_l_enveloppe_reste_done_avec_un_tableau_vide()
    {
        foreach (var env in new[] { Build(null), Build(Array.Empty<string>()), Build(new[] { "", "  ", "\n" }) })
        {
            Assert.Empty(env.Warnings);
            Assert.Equal("done", env.Status);
            using var doc = JsonDocument.Parse(Json(env));
            Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("warnings").ValueKind);
        }
    }

    [Fact]
    public void L_avertissement_pieces_non_disponibles_reste_en_premier_sans_doublon()
    {
        var inv = Sample();
        inv.Rooms = null;
        inv.Models = null;
        var env = Build(new[] { "autre" }, inv);
        Assert.Equal(2, env.Warnings.Count);
        Assert.StartsWith("Pièces non disponibles", env.Warnings[0]);
        Assert.Equal("autre", env.Warnings[1]);
        Assert.Equal("partial", env.Status);
    }

    [Fact]
    public void Doublons_retires_ordre_conserve()
    {
        var env = Build(new[] { "b", "a", "b", " a ", "c" });
        Assert.Equal(new[] { "b", "a", "c" }, env.Warnings);
    }

    [Fact]
    public void Nombre_borne_avec_une_ligne_de_synthese()
    {
        var many = Enumerable.Range(1, 50).Select(i => "avertissement " + i).ToList();
        var env = Build(many);
        Assert.Equal(EnvelopeWarnings.MaxCount, env.Warnings.Count);
        Assert.Equal(many.Take(EnvelopeWarnings.MaxCount - 1), env.Warnings.Take(EnvelopeWarnings.MaxCount - 1));
        Assert.Equal("… et 31 autre(s) avertissement(s), voir run.log.", env.Warnings[^1]);

        // Pile à la limite : rien n'est résumé.
        var exact = Build(many.Take(EnvelopeWarnings.MaxCount));
        Assert.Equal(many.Take(EnvelopeWarnings.MaxCount), exact.Warnings);
    }

    [Fact]
    public void Longueur_bornee_une_ligne_et_sans_coupure_de_paire_de_substitution()
    {
        var env = Build(new[] { new string('x', 5000), "ligne 1\r\nligne 2", new string('a', 398) + "😀😀" });
        Assert.All(env.Warnings, w => Assert.True(w.Length <= EnvelopeWarnings.MaxLength, "trop long : " + w.Length));
        Assert.Equal(EnvelopeWarnings.MaxLength, env.Warnings[0].Length);
        Assert.EndsWith("…", env.Warnings[0]);
        Assert.Equal("ligne 1  ligne 2", env.Warnings[1]);
        Assert.All(env.Warnings, w => Assert.DoesNotContain('\n', w));
        // Aucune demi-paire de substitution (le texte resterait invalide en UTF-8).
        foreach (var w in env.Warnings)
            for (var i = 0; i < w.Length; i++)
                if (char.IsHighSurrogate(w[i])) Assert.True(i + 1 < w.Length && char.IsLowSurrogate(w[i + 1]));
    }

    [Fact]
    public void Chemins_et_secrets_sont_masques_avant_d_arriver_dans_l_enveloppe()
    {
        var log = new RunLog();
        log.Warn("Ouverture de C:\\Users\\bob\\work\\Main.rvt échouée");
        log.Warn("chemin /tmp/da/work/input/Main.rvt introuvable");
        log.Warn("url https://bucket.s3.amazonaws.com/x?X-Amz-Signature=abc");
        log.Warn("token=supersecret");

        var json = Json(Build(log.WarningMessages));

        foreach (var forbidden in new[] { "bob", "C:\\\\Users", "/tmp/da", "amazonaws", "abc", "supersecret" })
            Assert.DoesNotContain(forbidden, json);
        Assert.Contains("<chemin>", json);
        Assert.Contains("<masqué>", json);
    }

    [Fact]
    public void RunLog_garde_tous_les_avertissements_meme_au_dela_de_la_limite_du_journal()
    {
        var log = new RunLog();
        for (var i = 0; i < RunLog.MaxLines + 20; i++) log.Warn("avertissement " + i);
        Assert.Equal(RunLog.MaxLines + 20, log.WarningMessages.Count);
        Assert.Equal(RunLog.MaxLines + 20, log.Warnings);
        Assert.Equal("avertissement 0", log.WarningMessages[0]);
        // Le journal est tronqué, mais l'enveloppe reste bornée.
        Assert.Equal(EnvelopeWarnings.MaxCount, Build(log.WarningMessages).Warnings.Count);
    }

    [Fact]
    public void Le_json_ecrit_porte_warnings_en_tableau_de_textes_et_le_statut()
    {
        var json = Json(Build(new[] { "un", "deux" }));
        using var doc = JsonDocument.Parse(json);
        var warnings = doc.RootElement.GetProperty("warnings");
        Assert.Equal(JsonValueKind.Array, warnings.ValueKind);
        Assert.All(warnings.EnumerateArray(), w => Assert.Equal(JsonValueKind.String, w.ValueKind));
        Assert.Equal("partial", doc.RootElement.GetProperty("status").GetString());
    }

    // ---------- identifiant Revit ----------

    [Fact]
    public void Chaque_element_porte_l_identifiant_revit_en_texte_lisible()
    {
        var env = Build(null);
        var elements = Elements(env).ToList();
        Assert.Equal(4, elements.Count); // 2001 sous deux systèmes (même objet), 2002, 2003

        foreach (var e in elements)
        {
            var expected = e.Id.Substring("element-".Length);
            var prop = Assert.Single(e.Properties, p => p.Key == "revitId");
            Assert.Equal("Identifiant Revit", prop.Label);
            Assert.Equal("text", prop.Type);
            Assert.Equal(expected, prop.Value);
            Assert.Null(prop.Unit);
        }
    }

    [Fact]
    public void Le_viewerId_et_la_maquette_ne_changent_pas_et_aucun_doublon_d_identifiant_unique()
    {
        var env = Build(null);
        var e2001 = Elements(env).First(e => e.Id == "element-2001");
        var e2003 = Elements(env).First(e => e.Id == "element-2003");

        Assert.Equal("eu-2001", Text(e2001, "viewerId"));
        Assert.Equal("Identifiant du viewer", e2001.Properties.First(p => p.Key == "viewerId").Label);
        Assert.Equal("main", Text(e2001, "modelKey"));
        Assert.Equal("Maquette", e2001.Properties.First(p => p.Key == "modelKey").Label);
        // L'UniqueId n'est porté que par viewerId : pas de propriété dupliquée.
        Assert.DoesNotContain(e2001.Properties, p => p.Key == "revitUniqueId");
        Assert.Single(e2001.Properties, p => p.Value is string v && v == "eu-2001");

        // UniqueId vide : pas de viewerId, mais revitId reste présent.
        Assert.Null(Text(e2003, "viewerId"));
        Assert.DoesNotContain(e2003.Properties, p => p.Key == "revitUniqueId");
        Assert.Equal("2003", Text(e2003, "revitId"));
    }

    [Fact]
    public void Les_systemes_et_les_pieces_ne_changent_pas()
    {
        var env = Build(null);
        Assert.All(env.Objects, s => Assert.DoesNotContain(s.Properties, p => p.Key == "revitId"));
        Assert.Equal("su-10", Text(env.Objects[0], "viewerId"));
    }

    // ---------- validité de l'enveloppe (règles de validateEnvelope du backend) ----------

    [Fact]
    public void L_enveloppe_reste_valide_selon_les_regles_du_backend()
    {
        var many = Enumerable.Range(1, 60).Select(i => "avertissement " + i).Append(new string('z', 900));
        var json = Json(Build(many));
        using var doc = JsonDocument.Parse(json);
        var errors = ValidateLikeBackend(doc.RootElement);
        Assert.Empty(errors);
    }

    /// <summary>Miroir minimal de <c>validateEnvelope</c> (src/envelope.ts du backend), hors serviceId ajouté par le backend.</summary>
    private static List<string> ValidateLikeBackend(JsonElement root)
    {
        var errors = new List<string>();
        if (root.GetProperty("contractVersion").GetString() is not { Length: > 0 }) errors.Add("contractVersion");
        var status = root.GetProperty("status").GetString();
        if (status is not ("done" or "partial")) errors.Add("status");
        var warnings = root.GetProperty("warnings");
        if (warnings.ValueKind != JsonValueKind.Array || warnings.EnumerateArray().Any(w => w.ValueKind != JsonValueKind.String)) errors.Add("warnings");
        else if (status == "partial" != (warnings.GetArrayLength() > 0)) errors.Add("status partial <=> warnings non vide");
        var ids = new Dictionary<string, string>();
        void Walk(JsonElement node)
        {
            var id = node.GetProperty("id").GetString()!;
            if (ids.TryGetValue(id, out var known)) { if (known != node.GetRawText()) errors.Add("id en double : " + id); return; }
            ids[id] = node.GetRawText();
            var keys = new HashSet<string>();
            foreach (var p in node.GetProperty("properties").EnumerateArray())
            {
                var type = p.GetProperty("type").GetString();
                var key = p.GetProperty("key").GetString()!;
                if (type != "ref" && !keys.Add(key)) errors.Add($"{id} : clé en double {key}");
                if (type == "text" && p.GetProperty("value").ValueKind != JsonValueKind.String) errors.Add($"{id}.{key} : texte attendu");
                if (key == "viewerId" && type != "text") errors.Add("viewerId");
                if (p.TryGetProperty("unit", out _) && type != "number") errors.Add($"{id}.{key} : unit réservé aux nombres");
            }
            foreach (var c in node.GetProperty("children").EnumerateArray()) Walk(c);
        }
        foreach (var o in root.GetProperty("objects").EnumerateArray()) Walk(o);
        return errors;
    }
}
