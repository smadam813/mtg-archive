namespace MtgArchive.Tests;

/// <summary>The committed plugin files, for tests over skill and reference text.</summary>
internal static class PluginFiles
{
    public static readonly string Root = Path.Combine(RepoRoot.Find(), "plugin", "mtg-archive");

    public static string Reference(string name) => File.ReadAllText(Path.Combine(Root, "references", name));

    public static string Skill(string name) => File.ReadAllText(Path.Combine(Root, "skills", name, "SKILL.md"));
}
