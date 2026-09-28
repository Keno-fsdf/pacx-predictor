namespace Pacx.Predictor.Tests;

public class PacHelpParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pac", name));

    [Fact]
    public void Root_page_lists_groups_without_description()
    {
        var page = PacHelpParser.Parse(Fixture("root.txt"));

        Assert.Null(page.Description);
        Assert.False(page.IsCommand);
        Assert.Contains(page.Usage, u => u.Name == "admin" && u.Optional);
        Assert.Contains(page.Usage, u => u.Name == "help");
        Assert.Equal("Work with your Power Platform Admin Account.", page.Entries.First(e => e.Name == "admin").Help);
    }

    [Fact]
    public void Wrapped_description_lines_are_joined()
    {
        var page = PacHelpParser.Parse(Fixture("root.txt"));
        var application = page.Entries.First(e => e.Name == "application");
        Assert.Contains("Microsoft Marketplace", application.Help);
    }

    [Fact]
    public void Group_page_has_description_and_children()
    {
        var page = PacHelpParser.Parse(Fixture("solution.txt"));

        Assert.Equal("Commands for working with Dataverse solution projects.", page.Description);
        Assert.False(page.IsCommand);
        Assert.Equal(18, page.Usage.Count);
        Assert.Equal("List all Solutions from the current Dataverse organization", page.Entries.First(e => e.Name == "list").Help);
    }

    [Fact]
    public void Command_page_parses_options_aliases_and_values()
    {
        var page = PacHelpParser.Parse(Fixture("admin-list.txt"));

        Assert.True(page.IsCommand);
        Assert.Equal("List all environments from your tenant.", page.Description);
        Assert.All(page.Usage, u => Assert.True(u.Optional));

        var type = page.Entries.First(e => e.Name == "--type");
        Assert.Equal("-t", type.Alias);
        Assert.Equal("List all environments with the given type.", type.Help);
        Assert.Equal(new[] { "Trial", "Sandbox", "Production", "Developer", "Teams", "SubscriptionBasedTrial" }, type.Values);

        var name = page.Entries.First(e => e.Name == "--name");
        Assert.Null(name.Values);
    }

    [Fact]
    public void Required_option_is_the_one_without_brackets()
    {
        var page = PacHelpParser.Parse(Fixture("org-update-settings.txt"));

        Assert.False(page.Usage.First(u => u.Name == "--name").Optional);
        Assert.True(page.Usage.First(u => u.Name == "--value").Optional);
        Assert.True(page.Usage.First(u => u.Name == "--environment").Optional);
    }

    [Fact]
    public void Empty_or_garbage_input_gives_an_empty_page()
    {
        var page = PacHelpParser.Parse("not a help page at all");
        Assert.Null(page.Description);
        Assert.Empty(page.Usage);
        Assert.Empty(page.Entries);
        Assert.True(page.IsCommand);

        Assert.Empty(PacHelpParser.Parse(null).Entries);
    }
}
