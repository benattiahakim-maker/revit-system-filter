using Egis.Revit.SystemFilter.Core.Services;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

public class BaseEquipmentFallbackTests
{
    [Fact]
    public void Aucun_candidat_donne_null() =>
        Assert.Null(BaseEquipmentFallback.Choose(Array.Empty<EquipmentCandidate>()));

    [Fact]
    public void Un_seul_candidat_est_retenu() =>
        Assert.Equal(42L, BaseEquipmentFallback.Choose(new[] { new EquipmentCandidate(42, 1) }));

    [Fact]
    public void Le_plus_raccorde_gagne()
    {
        var c = new[] { new EquipmentCandidate(10, 1), new EquipmentCandidate(30, 4), new EquipmentCandidate(20, 2) };
        Assert.Equal(30L, BaseEquipmentFallback.Choose(c));
    }

    [Fact]
    public void Egalite_departagee_par_le_plus_petit_id_quel_que_soit_l_ordre()
    {
        var a = new[] { new EquipmentCandidate(50, 3), new EquipmentCandidate(12, 3), new EquipmentCandidate(7, 1) };
        Assert.Equal(12L, BaseEquipmentFallback.Choose(a));
        Assert.Equal(12L, BaseEquipmentFallback.Choose(a.AsEnumerable().Reverse()));
    }

    [Fact]
    public void Doublons_fusionnes() =>
        Assert.Equal(5L, BaseEquipmentFallback.Choose(new[] { new EquipmentCandidate(9, 2), new EquipmentCandidate(5, 1), new EquipmentCandidate(5, 3) }));

    [Fact]
    public void Message_cite_systeme_et_equipement()
    {
        var m = BaseEquipmentFallback.Message(201, "Soufflage", 9001, 2);
        Assert.Contains("201", m);
        Assert.Contains("9001", m);
        Assert.Contains("repli", m);
    }

    [Fact]
    public void Synthese_au_dela_de_20_lignes()
    {
        var lines = Enumerable.Range(1, 25).Select(i => BaseEquipmentFallback.Message(i, $"S{i}", 1000 + i, 1)).ToList();
        var line = Assert.Single(BaseEquipmentFallback.Summarize(lines));
        Assert.StartsWith("25 systèmes", line);
        Assert.Equal(20, BaseEquipmentFallback.Summarize(lines.Take(20).ToList()).Count);
    }
}
