using Egis.Revit.SystemFilter.Core.Services;

namespace Egis.Revit.SystemFilter.DesignAutomation.Io.Tests;

public sealed class ParameterValueChoiceTests
{
    private static ParameterReading P(string? s, string? v = null) => new(true, s, v);

    [Fact]
    public void Instance_prime_sur_le_type()
        => Assert.Equal("ASF-01", ParameterValueChoice.Choose(P("ASF-01"), P("TYPE")));

    [Fact]
    public void Repli_sur_le_type_si_instance_absente()
        => Assert.Equal("CTA BUREAUX", ParameterValueChoice.Choose(ParameterReading.Missing, P("CTA BUREAUX")));

    [Fact]
    public void Repli_sur_le_type_si_instance_vide()
        => Assert.Equal("T", ParameterValueChoice.Choose(P("   "), P("T")));

    [Fact]
    public void ValueString_si_AsString_null()
        => Assert.Equal("12", ParameterValueChoice.Choose(P(null, "12"), ParameterReading.Missing));

    [Fact]
    public void ValueString_de_l_instance_avant_le_type()
        => Assert.Equal("12", ParameterValueChoice.Choose(P(null, "12"), P("T")));

    [Fact]
    public void Trim_et_vide_donne_null()
    {
        Assert.Equal("ASF-01", ParameterValueChoice.Choose(P("  ASF-01 "), ParameterReading.Missing));
        Assert.Null(ParameterValueChoice.Choose(P(" ", " "), P("", null)));
        Assert.Null(ParameterValueChoice.Choose(ParameterReading.Missing, ParameterReading.Missing));
    }

    [Fact]
    public void Synthese_warn_si_aucun_rempli()
    {
        var (info, warnings) = ParameterValueChoice.Summarize(37, 0, 0, 0, "EG-CVC_Num CTA", "NOM MATERIEL");
        Assert.Contains("équipements mécaniques : 37", info);
        Assert.Contains("numCta rempli : 0", info);
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("« EG-CVC_Num CTA »"));
        Assert.Contains(warnings, w => w.Contains("« NOM MATERIEL »"));
    }

    [Fact]
    public void Synthese_sans_warn_si_rempli_ou_aucun_equipement()
    {
        Assert.Empty(ParameterValueChoice.Summarize(5, 2, 1, 0, "A", "B").Warnings);
        Assert.Empty(ParameterValueChoice.Summarize(0, 0, 0, 0, "A", "B").Warnings);
    }
}
