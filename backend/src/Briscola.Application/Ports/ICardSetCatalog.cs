namespace Briscola.Application.Ports;

/// <summary>
/// Application-layer port for validating bundled card-set ids. The concrete
/// implementation (which scans <c>wwwroot/card-sets/</c>) lives in
/// <c>Briscola.Api</c>; the application layer never touches the filesystem
/// or HTTP. Use this from any service that needs to ensure a user-supplied
/// <c>activeCardSetId</c> is a known set.
/// </summary>
public interface ICardSetCatalog
{
    /// <summary>Stable id of the always-available fallback set.</summary>
    string DefaultSetId { get; }

    /// <summary>Returns <c>true</c> iff a set with this id is registered.</summary>
    bool Contains(string id);

    /// <summary>Returns the registered set ids in catalog order.</summary>
    IReadOnlyList<string> AllIds { get; }
}
