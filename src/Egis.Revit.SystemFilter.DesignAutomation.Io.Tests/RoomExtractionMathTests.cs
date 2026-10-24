using System.Text.Json;
using Egis.Revit.SystemFilter.Core.Models.Inventory;
using Egis.Revit.SystemFilter.Core.Services;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

/// <summary>Maths de boîtes et ids de pièces (format 1.1), sans Revit.</summary>
public class RoomExtractionMathTests
{
    private const double Mm = BoxMath.FeetToMm;
    private static readonly Vec3 Zero = new(0, 0, 0);

    private static void Near(double expected, double actual, double eps = 1e-6) => Assert.InRange(actual, expected - eps, expected + eps);

    [Fact]
    public void Identite_la_boite_reste_la_meme()
    {
        var (min, max) = BoxMath.TransformBox(new Vec3(1, 2, 3), new Vec3(4, 6, 8), Affine.Identity);
        Assert.Equal(new Vec3(1, 2, 3), min);
        Assert.Equal(new Vec3(4, 6, 8), max);
    }

    [Fact]
    public void Translation_decale_min_et_max()
    {
        var t = new Affine(new Vec3(10, -5, 2), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1));
        var (min, max) = BoxMath.TransformBox(new Vec3(0, 0, 0), new Vec3(1, 2, 3), t);
        Assert.Equal(new Vec3(10, -5, 2), min);
        Assert.Equal(new Vec3(11, -3, 5), max);
    }

    [Fact]
    public void Rotation_90_degres_autour_de_Z_echange_les_etendues_X_et_Y()
    {
        // X -> Y, Y -> -X (rotation de +90°)
        var rot = new Affine(Zero, new Vec3(0, 1, 0), new Vec3(-1, 0, 0), new Vec3(0, 0, 1));
        var (min, max) = BoxMath.TransformBox(new Vec3(0, 0, 0), new Vec3(4, 1, 2), rot);
        Assert.Equal(new Vec3(-1, 0, 0), min);
        Assert.Equal(new Vec3(0, 4, 2), max);
    }

    [Fact]
    public void Rotation_45_degres_agrandit_la_boite_alignee_sur_les_axes()
    {
        var c = Math.Sqrt(0.5);
        var rot = new Affine(Zero, new Vec3(c, c, 0), new Vec3(-c, c, 0), new Vec3(0, 0, 1));
        var (min, max) = BoxMath.TransformBox(new Vec3(0, 0, 0), new Vec3(2, 2, 1), rot);
        Near(-2 * c, min.X); Near(2 * c, max.X);
        Near(0, min.Y); Near(4 * c, max.Y);
        Near(0, min.Z); Near(1, max.Z);
    }

    [Fact]
    public void Chaine_boite_puis_lien_est_appliquee_dans_l_ordre()
    {
        var boxToModel = new Affine(new Vec3(1, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1));
        var rot = new Affine(new Vec3(100, 0, 0), new Vec3(0, 1, 0), new Vec3(-1, 0, 0), new Vec3(0, 0, 1));
        // (0,0,0)-(2,1,1) -> +1 en X : (1..3, 0..1) -> rotation : x' = 100 - y, y' = x
        var (min, max) = BoxMath.TransformBox(new Vec3(0, 0, 0), new Vec3(2, 1, 1), boxToModel, rot);
        Assert.Equal(new Vec3(99, 1, 0), min);
        Assert.Equal(new Vec3(100, 3, 1), max);
    }

    [Fact]
    public void ToHostMm_convertit_les_pieds_en_mm_et_arrondit_au_dixieme()
    {
        var link = new Affine(new Vec3(10, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1));
        var box = BoxMath.ToHostMm(new Vec3(0, 0, 0), new Vec3(1, 2, 3), Affine.Identity, link);
        Assert.Equal(new[] { 3048.0, 0.0, 0.0 }, box.Min);
        Assert.Equal(new[] { Math.Round(11 * Mm, 1), Math.Round(2 * Mm, 1), Math.Round(3 * Mm, 1) }, box.Max);
        Assert.Equal(304.8, BoxMath.Mm(1));
        Assert.Equal(0.3, BoxMath.Mm(0.001));
    }

    [Fact]
    public void Lien_identique_a_l_hote_ne_change_rien_et_min_reste_inferieur_a_max_apres_rotation()
    {
        var rot = new Affine(new Vec3(5, 5, 0), new Vec3(-1, 0, 0), new Vec3(0, -1, 0), new Vec3(0, 0, 1)); // 180°
        var box = BoxMath.ToHostMm(new Vec3(0, 0, 0), new Vec3(1, 1, 1), Affine.Identity, rot);
        for (var i = 0; i < 3; i++) Assert.True(box.Min[i] <= box.Max[i]);
        Assert.Equal(Math.Round(4 * Mm, 1), box.Min[0]);
        Assert.Equal(Math.Round(5 * Mm, 1), box.Max[0]);
    }

    [Fact]
    public void Ids_de_pieces_uniques_meme_si_les_ElementId_Revit_collisionnent_entre_maquettes()
    {
        var ids = new RoomIdAllocator();
        var a = ids.Allocate(ModelKeys.Host, 12345);
        var b = ids.Allocate(ModelKeys.ForLinkInstance(900), 12345);
        var c = ids.Allocate(ModelKeys.ForLinkInstance(901), 12345);
        Assert.Equal(new long[] { 1, 2, 3 }, new[] { a, b, c });
        Assert.Equal(a, ids.Allocate(ModelKeys.Host, 12345));
        Assert.Equal(4, ids.Allocate(ModelKeys.Host, 12346));
        Assert.Equal(4, ids.Count);
        Assert.Equal("link-900", ModelKeys.ForLinkInstance(900));
        Assert.Throws<ArgumentException>(() => ids.Allocate("", 1));
    }

    [Fact]
    public void Json_1_1_pieces_avec_null_explicites_bbox_des_elements_et_membres_omis_en_1_0()
    {
        var inventory = new SystemInventory
        {
            SchemaVersion = "1.1",
            Models = new List<ModelRecord> { new() { Key = "main", Title = "M", IsHost = true } },
            Rooms = new List<PlacedRoomRecord>
            {
                new() { Id = 1, RevitId = 7, UniqueId = "u", ModelKey = "main", Number = "001", Name = "Local", Area = 12.5, BBox = new BBoxRecord { Min = new[] { 0.0, 0, 0 }, Max = new[] { 1.0, 1, 1 } } },
            },
        };
        inventory.Elements.Add(new NetworkElementRecord { Id = 5, UniqueId = "e", Category = "DuctCurves" });
        var json = JsonSerializer.Serialize(inventory);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("1.1", root.GetProperty("schemaVersion").GetString());
        var room = root.GetProperty("rooms")[0];
        Assert.Equal(JsonValueKind.Null, room.GetProperty("volume").ValueKind);
        Assert.Equal(JsonValueKind.Null, room.GetProperty("department").ValueKind);
        Assert.Equal(12.5, room.GetProperty("area").GetDouble());
        Assert.Equal(1, room.GetProperty("id").GetInt64());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("elements")[0].GetProperty("bbox").ValueKind);
        Assert.True(root.GetProperty("models")[0].GetProperty("isHost").GetBoolean());
        Assert.Equal(1.0, root.GetProperty("models")[0].GetProperty("transform").GetProperty("basisX")[0].GetDouble());

        var v10 = JsonSerializer.Serialize(new SystemInventory(), new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        using var old = JsonDocument.Parse(v10);
        Assert.Equal("1.0", old.RootElement.GetProperty("schemaVersion").GetString());
        Assert.False(old.RootElement.TryGetProperty("rooms", out _));
        Assert.False(old.RootElement.TryGetProperty("models", out _));
    }
}
