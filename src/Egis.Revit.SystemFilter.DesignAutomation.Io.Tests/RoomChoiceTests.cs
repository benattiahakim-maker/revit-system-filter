using Egis.Revit.SystemFilter.Core.Models.Inventory;
using Egis.Revit.SystemFilter.Core.Services;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

public sealed class RoomChoiceTests
{
    private static RoomRecord R(long id) => new() { Id = id, Name = $"P{id}" };

    [Fact]
    public void Espace_d_abord()
    {
        var r = RoomChoice.Choose(R(1), R(2), R(3))!;
        Assert.Equal(1, r.Id);
        Assert.Equal("space", r.Source);
    }

    [Fact]
    public void Puis_piece_puis_piece_liee()
    {
        Assert.Equal("room", RoomChoice.Choose(null, R(2), R(3))!.Source);
        var linked = RoomChoice.Choose(null, null, R(3))!;
        Assert.Equal(("linkedRoom", 3L), (linked.Source, linked.Id));
    }

    [Fact]
    public void Rien_trouve_donne_null()
        => Assert.Null(RoomChoice.Choose(null, null, null));

    [Fact]
    public void Synthese()
    {
        var eq = new[]
        {
            new NetworkElementRecord { Room = new RoomRecord { Source = "space" } },
            new NetworkElementRecord { Room = new RoomRecord { Source = "linkedRoom" } },
            new NetworkElementRecord(),
        };
        Assert.Equal("pièces : équipements mécaniques avec pièce : 2 (space 1, room 0, linkedRoom 1) ; sans pièce : 1", RoomChoice.Summary(eq));
    }
}
