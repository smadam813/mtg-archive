using System.Text.RegularExpressions;
using MtgArchive.Api;

namespace MtgArchive.Tests;

/// <summary>
/// The citation type list appears in the enum and in four docs. A change left
/// <c>run</c> in one of them after the enum dropped it.
/// </summary>
public sealed partial class CitationDocsTests
{
    private static readonly string[] Expected =
        [.. Enum.GetNames<CitationType>().Select(n => SnakeCase().Replace(n, "$1_$2").ToLowerInvariant())];

    [Fact]
    public void The_api_conventions_doc_lists_every_citation_type()
        => Assert.Equal(Expected, PipeList(Path.Combine("docs", "api-conventions.md")));

    [Fact]
    public void The_answer_contract_lists_every_citation_type()
        => Assert.Equal(Expected, PipeList(Path.Combine("plugin", "mtg-archive", "references", "answer-contract.md")));

    [Fact]
    public void The_glossary_lists_every_citation_type()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot.Find(), "GLOSSARY.md"));
        var match = GlossaryList().Match(text);
        Assert.True(match.Success, "GLOSSARY.md lost the Citation type list");

        var listed = match.Groups[1].Value.Split(',')
            .Select(s => s.Trim().Replace("or ", "", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(Expected, listed);
    }

    private static string[] PipeList(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot.Find(), relativePath));
        var match = PipeType().Match(text);
        Assert.True(match.Success, $"{relativePath} lost the citation type line");
        return match.Groups[1].Value.Split('|');
    }

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex SnakeCase();

    [GeneratedRegex(@"\{ ""type"": ""(card\|[a-z_|]+)"", ""id""")]
    private static partial Regex PipeType();

    [GeneratedRegex(@"with type one of ([a-z_, ]+)\.")]
    private static partial Regex GlossaryList();
}
