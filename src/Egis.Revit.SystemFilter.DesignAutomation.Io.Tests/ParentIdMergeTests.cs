using Egis.Revit.SystemFilter.Core.Services;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

public class ParentIdMergeTests
{
    [Fact]
    public void Deux_systemes_sont_tous_deux_conserves()
    {
        var ids = new List<long> { 201 };
        Assert.True(ParentIdMerge.Add(ids, 5012));
        Assert.Equal(new long[] { 201, 5012 }, ids);
    }

    [Fact]
    public void Doublon_ignore()
    {
        var ids = new List<long> { 201 };
        Assert.False(ParentIdMerge.Add(ids, 201));
        Assert.Equal(new long[] { 201 }, ids);
    }

    [Fact]
    public void Ordre_de_premiere_rencontre_stable()
    {
        var ids = new List<long> { 30 };
        foreach (var id in new long[] { 10, 30, 20, 10, 20 }) ParentIdMerge.Add(ids, id);
        Assert.Equal(new long[] { 30, 10, 20 }, ids);
    }
}
