using System.Text.RegularExpressions;

namespace MtgArchive.Tests;

/// <summary>
/// The code under <c>Simulation/Engine/</c> names no type of
/// <c>Simulation/Xmage/</c>: the pilot, the goal and the batch runner read
/// the pilot view only (<c>.claude/rules/simulation.md</c>).
/// </summary>
public sealed partial class EngineNeutralityTests
{
    [Fact]
    public void Engine_code_names_no_xmage_namespace()
    {
        var root = Path.Combine(RepoRoot.Find(), "src", "MtgArchive", "Simulation", "Engine");
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file).Select((text, index) => (File: Path.GetFileName(file), Line: index + 1, Text: text)))
            .Where(line => ForbiddenReference().IsMatch(line.Text))
            .Select(line => $"{line.File}:{line.Line}: {line.Text.Trim()}")
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>A global using would let Engine code name an XMage type
    /// bare, which the line check above cannot see.</summary>
    [Fact]
    public void No_file_of_the_service_global_imports_the_xmage_namespace()
    {
        var src = Path.Combine(RepoRoot.Find(), "src", "MtgArchive");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => File.ReadLines(file).Where(line => GlobalUsing().IsMatch(line)).Select(line => $"{Path.GetFileName(file)}: {line.Trim()}"))
            .ToList();
        var project = File.ReadAllText(Path.Combine(src, "MtgArchive.csproj"));

        Assert.Empty(offenders);
        Assert.DoesNotMatch(@"<Using\s+Include=""MtgArchive\.Simulation\.Xmage""", project);
    }

    [GeneratedRegex(@"\bSimulation\.Xmage\b")]
    private static partial Regex ForbiddenReference();

    [GeneratedRegex(@"^\s*global\s+using\s+(static\s+)?(\w+\s*=\s*)?MtgArchive\.Simulation\.Xmage\b")]
    private static partial Regex GlobalUsing();
}
