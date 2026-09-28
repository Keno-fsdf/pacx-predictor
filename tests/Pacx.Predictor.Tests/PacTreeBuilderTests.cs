namespace Pacx.Predictor.Tests;

public class PacTreeBuilderTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pac", name));

    /// <summary>
    /// A small pac made of real help pages: two groups, one of them with a nested group,
    /// one command without options, one chain that fails.
    /// </summary>
    private static string? FakeHelp(IReadOnlyList<string> chain) => string.Join(' ', chain) switch
    {
        "" => """
              Usage: pac [admin] [org] [auth] [help]
                admin                       Work with your Power Platform Admin Account.
                org                         Work with your Dataverse organization.
                auth                        Manage how you authenticate to various services.
                help                        Show help for the Microsoft Power Platform CLI.
              """,
        "admin" => """
                   Help:
                   Work with your Power Platform Admin Account.

                   Commands:
                   Usage: pac admin [list] [application]

                     list                        List all environments from your tenant.
                     application                 Commands for managing Dataverse applications.
                   """,
        "admin list" => Fixture("admin-list.txt"),
        "admin application" => """
                               Help:
                               Commands for managing Dataverse applications.

                               Commands:
                               Usage: pac admin application [list]

                                 list                        List installed applications.
                               """,
        "admin application list" => """
                                    Help:
                                    List installed applications.

                                    Commands:
                                    Usage: pac admin application list [--environment]

                                      --environment               Target environment. (alias: -env)
                                    """,
        "org" => """
                 Help:
                 Work with your Dataverse organization.

                 Commands:
                 Usage: pac org [who] [update-settings]

                   who                         Displays information about the current Dataverse organization.
                   update-settings             Update environment settings
                 """,
        "org who" => """
                     Help:
                     Displays information about the current Dataverse organization.

                     Commands:
                     Usage: pac org who
                     """,
        "org update-settings" => Fixture("org-update-settings.txt"),
        "auth" => null,   // pac could not be run for this one
        _ => null,
    };

    private static CommandTree Build() => PacTreeBuilder.Build(FakeHelp);

    [Fact]
    public void Groups_become_namespaces_and_commands_keep_their_chain()
    {
        var tree = Build();

        Assert.Contains(tree.Namespaces, n => n.Verbs.SequenceEqual(new[] { "admin" }));
        Assert.Contains(tree.Namespaces, n => n.Verbs.SequenceEqual(new[] { "admin", "application" }));
        Assert.Contains(tree.Namespaces, n => n.Verbs.SequenceEqual(new[] { "org" }));

        Assert.Contains(tree.Commands, c => c.Verbs.SequenceEqual(new[] { "admin", "list" }));
        Assert.Contains(tree.Commands, c => c.Verbs.SequenceEqual(new[] { "admin", "application", "list" }));
        Assert.Contains(tree.Commands, c => c.Verbs.SequenceEqual(new[] { "org", "who" }));
        Assert.Contains(tree.Commands, c => c.Verbs.SequenceEqual(new[] { "org", "update-settings" }));
    }

    [Fact]
    public void Help_command_and_failed_pages_are_left_out()
    {
        var tree = Build();

        Assert.DoesNotContain(tree.Commands, c => c.Verbs.Contains("help"));
        Assert.DoesNotContain(tree.Namespaces, n => n.Verbs.Contains("help"));
        Assert.DoesNotContain(tree.Namespaces, n => n.Verbs.Contains("auth"));
        Assert.DoesNotContain(tree.Commands, c => c.Verbs.Contains("auth"));
    }

    [Fact]
    public void Options_carry_required_alias_and_values()
    {
        var tree = Build();

        var update = tree.Commands.First(c => c.Verbs.SequenceEqual(new[] { "org", "update-settings" }));
        var name = update.Options.First(o => o.Long == "name");
        Assert.True(name.Required);
        Assert.Equal("n", name.Short);
        Assert.False(update.Options.First(o => o.Long == "value").Required);

        var list = tree.Commands.First(c => c.Verbs.SequenceEqual(new[] { "admin", "list" }));
        Assert.Equal(new[] { "Trial", "Sandbox", "Production", "Developer", "Teams", "SubscriptionBasedTrial" },
            list.Options.First(o => o.Long == "type").Values);
    }

    [Fact]
    public void Command_without_options_is_still_a_command()
    {
        var who = Build().Commands.First(c => c.Verbs.SequenceEqual(new[] { "org", "who" }));
        Assert.Empty(who.Options);
        Assert.Equal("Displays information about the current Dataverse organization.", who.Help);
    }

    [Fact]
    public void Namespace_help_comes_from_the_page_or_the_parent_listing()
    {
        var tree = Build();
        Assert.Equal("Work with your Dataverse organization.", tree.Namespaces.First(n => n.Verbs[0] == "org").Help);
    }

    [Fact]
    public void Deprecated_groups_are_not_fetched()
    {
        var fetched = new List<string>();
        var tree = PacTreeBuilder.Build(chain =>
        {
            fetched.Add(string.Join(' ', chain));
            return string.Join(' ', chain) switch
            {
                "" => "Usage: pac [org] [test]\n  org   Work with your organization.\n  test   (Deprecated) Old test runner.\n",
                "org" => "Usage: pac org [who]\n  who   Who am I.\n",
                "org who" => "Usage: pac org who\n",
                _ => null,
            };
        });

        Assert.DoesNotContain("test", fetched);
        Assert.Single(tree.Commands);
    }

    [Fact]
    public void Cache_key_carries_the_newest_installed_pac_version()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pacx-predictor-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "pac.cmd"), "@echo off");
            Directory.CreateDirectory(Path.Combine(dir, "Microsoft.PowerApps.CLI.2.8.1"));
            Directory.CreateDirectory(Path.Combine(dir, "Microsoft.PowerApps.CLI.2.12.2"));
            Directory.CreateDirectory(Path.Combine(dir, "Microsoft.PowerApps.CLI.Core.1.0.0"));   // not a CLI version

            Assert.Equal("pac|Microsoft.PowerApps.CLI.2.12.2", PacTreeLoader.ComputeKey(dir));

            Directory.CreateDirectory(Path.Combine(dir, "Microsoft.PowerApps.CLI.2.13.0"));
            Assert.Equal("pac|Microsoft.PowerApps.CLI.2.13.0", PacTreeLoader.ComputeKey(dir));

            Assert.Equal("pac:not-found", PacTreeLoader.ComputeKey(Path.Combine(dir, "nowhere")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Cache_key_does_not_depend_on_where_or_how_pac_is_spelled_on_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pacx-predictor-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "pac.cmd"), "@echo off");
            Directory.CreateDirectory(Path.Combine(dir, "Microsoft.PowerApps.CLI.2.12.2"));

            // Two shells that spell the same PATH entry differently must share one cache.
            var key = PacTreeLoader.ComputeKey(dir);
            Assert.Equal(key, PacTreeLoader.ComputeKey(dir.ToUpperInvariant()));
            Assert.Equal(key, PacTreeLoader.ComputeKey(dir + Path.DirectorySeparatorChar));
            Assert.DoesNotContain(dir, key, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Build_reports_every_page_it_fetches()
    {
        var reported = new List<int>();
        PacTreeBuilder.Build(FakeHelp, n => { lock (reported) reported.Add(n); });

        // Everything below the root: admin, org, auth, admin list, admin application,
        // org who, org update-settings, admin application list.
        Assert.Equal(8, reported.Count);
        Assert.Equal(8, reported.Max());
    }

    [Fact]
    public void Only_one_process_gets_the_build_gate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pacx-predictor-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using (var first = BuildGate.TryAcquire(dir))
            {
                Assert.NotNull(first);
                Assert.Null(BuildGate.TryAcquire(dir));
            }
            using var again = BuildGate.TryAcquire(dir);
            Assert.NotNull(again);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Deprecated_options_are_dropped()
    {
        var page = PacHelpParser.Parse(Fixture("auth-create.txt"));
        var tree = PacTreeBuilder.Build(chain => chain.Count == 0
            ? "Usage: pac [auth]\n  auth   Manage how you authenticate.\n"
            : string.Join(' ', chain) == "auth"
                ? "Usage: pac auth [create]\n  create   Create and store authentication profiles.\n"
                : Fixture("auth-create.txt"));

        var create = tree.Commands.Single();
        Assert.DoesNotContain(create.Options, o => o.Long == "url");
        Assert.Contains(create.Options, o => o.Long == "cloud" && o.Values!.Contains("UsGov"));
        Assert.Equal(page.Entries.Count(e => !e.Help.StartsWith("(deprecated)")), create.Options.Count);
    }

    [Fact]
    public void Tree_survives_a_json_roundtrip_through_the_cache_format()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Build());
        var back = CommandTree.Parse(json);

        Assert.NotNull(back);
        Assert.Equal(Build().Commands.Count, back!.Commands.Count);
        Assert.True(back.Commands.First(c => c.Verbs.SequenceEqual(new[] { "org", "update-settings" }))
            .Options.First(o => o.Long == "name").Required);
    }

    [Fact]
    public void Unlisted_org_alias_gets_the_env_suggestions()
    {
        var tree = PacTreeBuilder.Build(chain => string.Join(' ', chain) switch
        {
            "" => "Usage: pac [env]\n  env   Work with your Dataverse organization.\n",
            "env" => "Help: \nWork with your Dataverse organization.\n\nCommands: \nUsage: pac env [who] [update-settings]\n\n  who   Who am I.\n  update-settings   Update settings.\n",
            "env who" => "Usage: pac env who\n",
            "env update-settings" => Fixture("org-update-settings.txt"),
            _ => null,
        });

        PacTreeBuilder.AddKnownAliases(tree);
        PacTreeBuilder.AddKnownAliases(tree);   // second call must not duplicate anything

        var update = tree.Commands.First(c => c.Verbs.SequenceEqual(new[] { "env", "update-settings" }));
        Assert.Single(update.Aliases);
        Assert.Equal(new[] { "org", "update-settings" }, update.Aliases[0]);
        Assert.Single(tree.Namespaces, n => n.Verbs.SequenceEqual(new[] { "org" }));

        var names = PacPredictor.Names;
        Assert.Contains(SuggestionEngine.Suggest("pac org up", tree, commandNames: names), s => s.Text == "pac org update-settings");
        Assert.Equal("pac org update-settings --name", SuggestionEngine.Suggest("pac org update-settings ", tree, commandNames: names)[0].Text);
        Assert.Contains(SuggestionEngine.Suggest("pac ", tree, commandNames: names), s => s.Text == "pac org");
    }

    [Fact]
    public void Completer_returns_only_the_word_that_replaces_the_typed_one()
    {
        var tree = Build();

        var verb = PacCompleter.Completions("pac org up", tree);
        Assert.Equal("update-settings", Assert.Single(verb).Text);
        Assert.Contains("settings", verb[0].Tooltip, StringComparison.OrdinalIgnoreCase);

        // After a space: everything on that level, required options first.
        Assert.Equal(new[] { "who", "update-settings" }, PacCompleter.Completions("pac org ", tree).Select(c => c.Text));
        Assert.Equal("--name", PacCompleter.Completions("pac org update-settings ", tree)[0].Text);

        var values = PacCompleter.Completions("pac admin list --type S", tree).Select(c => c.Text).ToList();
        Assert.Equal(new[] { "Sandbox", "SubscriptionBasedTrial" }, values);

        // A tooltip is mandatory for PowerShell, so the word stands in when there is no help.
        Assert.All(PacCompleter.Completions("pac ", tree), c => Assert.False(string.IsNullOrWhiteSpace(c.Tooltip)));

        Assert.Empty(PacCompleter.Completions("pacx org up", tree));
    }

    [Fact]
    public void Engine_suggests_pac_commands_and_ignores_pacx_lines()
    {
        var tree = Build();

        var verbs = SuggestionEngine.Suggest("pac org up", tree, commandNames: PacPredictor.Names);
        Assert.Contains(verbs, s => s.Text == "pac org update-settings");

        var options = SuggestionEngine.Suggest("pac org update-settings ", tree, commandNames: PacPredictor.Names);
        Assert.Equal("pac org update-settings --name", options[0].Text);   // required first

        var values = SuggestionEngine.Suggest("pac admin list --type ", tree, commandNames: PacPredictor.Names);
        Assert.Contains(values, s => s.Text == "pac admin list --type Sandbox");

        Assert.Empty(SuggestionEngine.Suggest("pacx org up", tree, commandNames: PacPredictor.Names));
        Assert.Empty(SuggestionEngine.Suggest("pac org up", tree));   // default names are pacx
    }
}
