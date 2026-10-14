namespace MtgArchive.Tests;

/// <summary>The checkout root, for tests over committed files.</summary>
internal static class RepoRoot
{
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MtgArchive.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException($"No MtgArchive.sln above {AppContext.BaseDirectory}.");
    }
}
