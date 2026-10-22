using Egis.Revit.SystemFilter.Aps.Tests;
using Egis.Revit.SystemFilter.WorkItemRunner.Services;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Tests;

public sealed class ChecksTests
{
    [Fact]
    public void Missing_or_absent_model_is_rejected()
    {
        Assert.False(InputChecks.CheckModel("").IsValid);
        Assert.False(InputChecks.CheckModel(Path.Combine(Path.GetTempPath(), "inexistant.rvt")).IsValid);
    }

    [Fact]
    public void Existing_model_is_accepted_non_rvt_warns()
    {
        using var temp = new TempDirectory();
        var rvt = Path.Combine(temp.Path, "maquette.rvt");
        File.WriteAllText(rvt, "RVT");
        Assert.True(InputChecks.CheckModel(rvt).IsValid);

        var other = Path.Combine(temp.Path, "maquette.txt");
        File.WriteAllText(other, "x");
        var check = InputChecks.CheckModel(other);
        Assert.True(check.IsValid);
        Assert.NotEmpty(check.Warnings);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("{ \"includeDuctSystems\": true }", true)]
    [InlineData("{ pas du json", false)]
    [InlineData("[]", false)]
    public void Options_are_optional_but_must_be_a_json_object(string options, bool valid)
    {
        Assert.Equal(valid, InputChecks.CheckOptions(options).IsValid);
    }
}
