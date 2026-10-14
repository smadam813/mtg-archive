namespace MtgArchive.Api;

/// <summary>
/// Binding convention for every versioned input (<c>banlist_version</c>,
/// bracket spec version, GC snapshot): the pin is
/// optional and defaults to latest; the endpoint must echo the resolved
/// version in the response's versions block; an unknown pin is a 400 problem+json
/// (<see cref="Problems.UnknownVersion"/> thrown as a <see cref="ProblemException"/>)
/// — never a silent fallback.
/// </summary>
public static class VersionParameter
{
    /// <summary>
    /// <paramref name="latest"/> is null only while the axis has no versions
    /// at all (nothing ingested yet): omitting the pin then resolves to no
    /// version, and any pin is unknown. A null <paramref name="latest"/>
    /// alongside a non-empty <paramref name="known"/> (or the reverse) is a
    /// caller bug — resolving it would silently drop the latest-by-default
    /// contract, so it throws instead.
    /// </summary>
    public static string? Resolve(string parameter, string? pinned, string? latest, IReadOnlyCollection<string> known)
    {
        if ((latest is null) != (known.Count == 0))
        {
            throw new InvalidOperationException(
                $"{parameter}: latest must be present exactly when the axis has versions.");
        }

        if (pinned is null)
        {
            return latest;
        }

        return known.Contains(pinned)
            ? pinned
            : throw new ProblemException(Problems.UnknownVersion(parameter, pinned));
    }
}
