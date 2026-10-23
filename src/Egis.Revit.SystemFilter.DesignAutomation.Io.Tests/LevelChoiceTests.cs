using Egis.Revit.SystemFilter.Core.Services;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

public sealed class LevelChoiceTests
{
    private static readonly LevelInfo[] Levels = { new("L2", 10), new("L1", 0), new("L3", 20) };

    [Fact]
    public void Premier_candidat_non_vide_dans_l_ordre()
        => Assert.Equal("L3", LevelChoice.Choose(new string?[] { null, " ", "L3", "L1" }, Levels, 5));

    [Fact]
    public void Repli_altitude_niveau_le_plus_proche_dessous()
    {
        Assert.Equal("L2", LevelChoice.Choose(new string?[] { null }, Levels, 15));
        Assert.Equal("L2", LevelChoice.Choose(new string?[0], Levels, 9.995)); // tolérance
        Assert.Equal("L3", LevelChoice.Choose(new string?[0], Levels, 99));
    }

    [Fact]
    public void Sous_tous_les_niveaux_donne_le_plus_bas()
        => Assert.Equal("L1", LevelChoice.Choose(new string?[0], Levels, -5));

    [Fact]
    public void Null_sans_candidat_ni_altitude_ni_niveaux()
    {
        Assert.Null(LevelChoice.Choose(new string?[] { null }, Levels, null));
        Assert.Null(LevelChoice.Choose(new string?[0], new LevelInfo[0], 3));
    }

    [Fact]
    public void Tri_par_elevation_et_dedoublonnage()
        => Assert.Equal(new[] { "L1", "L2", "L3", "Inconnu" },
            LevelChoice.SortDistinct(new string?[] { "L3", "L1", null, "L2", "L1", "Inconnu", " " }, Levels));

    [Fact]
    public void Synthese()
        => Assert.Equal("niveaux : 10 éléments ; sans niveau avant repli : 4 ; après repli (hôte / altitude) : 1",
            LevelChoice.Summary(10, 4, 1));
}
