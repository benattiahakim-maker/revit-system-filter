namespace Egis.Revit.SystemFilter.Aps;

/// <summary>Fournit un jeton d'accès 2-legged valide.</summary>
public interface IApsTokenProvider
{
    ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Jeton fourni tel quel (<c>APS_TOKEN</c>), jamais renouvelé.</summary>
public sealed class StaticTokenProvider(string token) : IApsTokenProvider
{
    private readonly string _token = string.IsNullOrEmpty(token) ? throw new ArgumentException("Jeton vide.", nameof(token)) : token;

    public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_token);

    public override string ToString() => "StaticTokenProvider { Token = *** }";
}
