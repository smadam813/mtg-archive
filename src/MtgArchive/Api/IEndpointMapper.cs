namespace MtgArchive.Api;

/// <summary>
/// Composition seam for the host: Program maps every registered
/// <see cref="IEndpointMapper"/> at startup. Domain endpoints register
/// through it, and the test host registers its seam probes the same way,
/// which keeps test scaffolding out of this assembly while the probes still
/// run inside the real pipeline (routing, JSON options, exception handling).
/// </summary>
public interface IEndpointMapper
{
    void Map(IEndpointRouteBuilder app);
}
