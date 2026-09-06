using Xunit;

namespace Pacx.Predictor.Tests;

public class SuggestionEngineTests
{
    private static readonly CommandTree Tree = LoadFixture();

    private static CommandTree LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "pacx-tree.json");
        var tree = CommandTree.Parse(File.ReadAllText(path));
        Assert.NotNull(tree);
        return tree!;
    }

    private static IReadOnlyList<string> Texts(string input, int max = 50) =>
        SuggestionEngine.Suggest(input, Tree, max).Select(s => s.Text).ToList();

    [Fact]
    public void Fixture_parses_real_export()
    {
        Assert.True(Tree.Commands.Count > 100);
        Assert.True(Tree.Namespaces.Count > 20);
        Assert.Contains(Tree.Commands, c => c.Verbs.SequenceEqual(new[] { "auth", "create" }));
    }

    [Fact]
    public void Parse_tolerates_banner_lines_around_json()
    {
        var raw = "Some banner\n{\"commands\":[{\"verbs\":[\"a\",\"b\"]}],\"namespaces\":[]}\ntrailing";
        var tree = CommandTree.Parse(raw);
        Assert.NotNull(tree);
        Assert.Single(tree!.Commands);
        Assert.Empty(tree.Commands[0].Options);
    }

    [Fact]
    public void Parse_survives_control_characters_from_console_encoding()
    {
        // pacx help texts can contain characters the OEM code page maps to 0x1A.
        var raw = "{\"commands\":[{\"verbs\":[\"x\"],\"help\":\"bad \u001A char\"}],\"namespaces\":[]}\r\n";
        var tree = CommandTree.Parse(raw);
        Assert.NotNull(tree);
        Assert.Equal("bad   char", tree!.Commands[0].Help);
    }

    [Fact]
    public void Parse_returns_null_for_garbage()
    {
        Assert.Null(CommandTree.Parse(null));
        Assert.Null(CommandTree.Parse("no json here"));
        Assert.Null(CommandTree.Parse("{ not json }"));
    }

    [Fact]
    public void Ignores_non_pacx_input()
    {
        Assert.Empty(Texts("git status"));
        Assert.Empty(Texts(""));
        Assert.Empty(Texts("pac auth"));
    }

    [Fact]
    public void Suggests_top_level_verbs_after_pacx()
    {
        var texts = Texts("pacx ");
        Assert.Contains("pacx auth", texts);
        Assert.Contains("pacx solution", texts);
        Assert.DoesNotContain(texts, t => t.Contains('!'));
    }

    [Fact]
    public void Completes_partial_verb_and_keeps_typed_prefix()
    {
        var texts = Texts("pacx au");
        Assert.Contains("pacx auth", texts);
        Assert.All(texts, t => Assert.StartsWith("pacx au", t));
    }

    [Fact]
    public void Suggests_second_level_verbs()
    {
        var texts = Texts("pacx auth ");
        Assert.Contains("pacx auth create", texts);
        Assert.All(texts, t => Assert.StartsWith("pacx auth ", t));
    }

    [Fact]
    public void Suggests_required_options_first_for_resolved_command()
    {
        var texts = Texts("pacx auth create ");
        Assert.NotEmpty(texts);
        Assert.Equal("pacx auth create --name", texts[0]);
        Assert.Contains("pacx auth create --environment", texts);
    }

    [Fact]
    public void Does_not_repeat_used_options()
    {
        var texts = Texts("pacx auth create --name demo ");
        Assert.DoesNotContain("pacx auth create --name demo --name", texts);
        Assert.Contains("pacx auth create --name demo --environment", texts);
    }

    [Fact]
    public void Short_option_counts_as_used()
    {
        var texts = Texts("pacx auth create -n demo ");
        Assert.DoesNotContain(texts, t => t.EndsWith("--name"));
    }

    [Fact]
    public void Completes_partial_option()
    {
        var texts = Texts("pacx auth create --env");
        Assert.Equal(new[] { "pacx auth create --environment" }, texts);
    }

    [Fact]
    public void Nothing_after_option_that_expects_free_text()
    {
        Assert.Empty(Texts("pacx auth create --name "));
    }

    [Fact]
    public void Suggests_enum_values_after_option_with_values()
    {
        var (cmd, opt) = Tree.Commands
            .SelectMany(c => c.Options.Select(o => (c, o)))
            .First(x => x.o.Values is { Count: > 1 });

        var line = "pacx " + string.Join(' ', cmd.Verbs) + " " + opt.LongToken + " ";
        var texts = Texts(line);
        Assert.Equal(opt.Values!.Take(texts.Count), texts.Select(t => t[line.Length..]));
    }

    [Fact]
    public void Resolves_aliases_to_their_command()
    {
        var aliased = Tree.Commands.FirstOrDefault(c => c.Aliases.Any(a => a.Count > 0) && c.Options.Count > 0);
        if (aliased is null) return; // nothing to test in this export

        var alias = aliased.Aliases.First(a => a.Count > 0);
        var texts = Texts("pacx " + string.Join(' ', alias) + " ");
        Assert.NotEmpty(texts);
        Assert.All(texts, t => Assert.Contains("--", t));
    }

    [Fact]
    public void Respects_max()
    {
        Assert.True(Texts("pacx ", max: 3).Count <= 3);
    }

    [Fact]
    public void Tokenizer_keeps_quoted_strings_and_detects_trailing_space()
    {
        var tokens = SuggestionEngine.Tokenize("pacx auth create --name \"my env\" ", out var trailing);
        Assert.Equal(new[] { "pacx", "auth", "create", "--name", "\"my env\"" }, tokens);
        Assert.True(trailing);

        tokens = SuggestionEngine.Tokenize("pacx au", out trailing);
        Assert.Equal(new[] { "pacx", "au" }, tokens);
        Assert.False(trailing);
    }

    [Fact]
    public void Suggestion_is_fast_enough_for_a_keystroke()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 200; i++)
        {
            SuggestionEngine.Suggest("pacx solution ", Tree);
            SuggestionEngine.Suggest("pacx auth create --", Tree);
        }
        sw.Stop();
        // 400 calls; PSReadLine budgets ~20 ms per keystroke.
        Assert.True(sw.ElapsedMilliseconds / 400.0 < 5, $"avg {sw.ElapsedMilliseconds / 400.0} ms");
    }
}
