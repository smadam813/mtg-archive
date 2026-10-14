namespace MtgArchive.Api;

/// <summary>
/// One <c>name_rewrites</c> entry: a name as sent that resolved only without
/// its <c>A-</c> prefix, and the card it resolved to. A deck answer reports
/// it under Assumptions. The Simulation, Bracket, and Cards slices all put it
/// on the wire.
/// </summary>
public sealed record NameRewrite(string Name, Citation Card);
