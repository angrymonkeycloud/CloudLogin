using AngryMonkey.CloudLogin.Cli;

namespace AngryMonkey.CloudLogin.Tests.Cli;

public class CliArgumentsTests
{
    [Theory]
    [InlineData]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("-?")]
    [InlineData("/?")]
    public void NoArgumentsOrAHelpSwitch_IsTheHelpCommand(params string[] args) =>
        Assert.Equal("help", CliArguments.Parse(args).Command);

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    [InlineData("version")]
    public void TheVersionSwitches_AreTheVersionCommand(string argument) =>
        Assert.Equal("version", CliArguments.Parse([argument]).Command);

    [Theory]
    [InlineData("login")]
    [InlineData("LOGIN")]
    [InlineData("Status")]
    [InlineData("token")]
    [InlineData("call")]
    [InlineData("logout")]
    [InlineData("scenario")]
    public void CommandNames_AreCaseInsensitive(string command) =>
        Assert.Equal(command.ToLowerInvariant(), CliArguments.Parse([command]).Command);

    [Theory]
    [InlineData("signin")]
    [InlineData("log-in")]
    [InlineData("--app")]
    [InlineData("")]
    public void AnUnknownCommand_IsAUsageError(string command)
    {
        CliUsageException error = Assert.Throws<CliUsageException>(() => CliArguments.Parse([command]));
        Assert.Contains("Unknown command", error.Message);
    }

    [Fact]
    public void AnOptionWithASpace_TakesTheNextArgument()
    {
        CliArguments arguments = CliArguments.Parse(["login", "--app", "https://app.test", "--scheme", "blusky"]);

        Assert.Equal("https://app.test", arguments.Get("app"));
        Assert.Equal("blusky", arguments.Get("scheme"));
    }

    [Fact]
    public void AnOptionWithAnEqualsSign_TakesItsOwnValue()
    {
        CliArguments arguments = CliArguments.Parse(["login", "--app=https://app.test/?x=1", "--scheme=blusky"]);

        Assert.Equal("https://app.test/?x=1", arguments.Get("app"));
        Assert.Equal("blusky", arguments.Get("scheme"));
    }

    [Fact]
    public void OptionNames_AreCaseInsensitive() =>
        Assert.Equal("blusky", CliArguments.Parse(["login", "--SCHEME", "blusky"]).Get("scheme"));

    [Fact]
    public void AValueMayContainSpaces() =>
        Assert.Equal("Ada Lovelace", CliArguments.Parse(["login", "--test-user", "Ada Lovelace"]).Get("test-user"));

    [Fact]
    public void AnEmptyValueAfterEquals_IsKeptEmpty() =>
        Assert.Equal(string.Empty, CliArguments.Parse(["login", "--test-user="]).Get("test-user"));

    [Theory]
    [InlineData("login", "--audience")]
    [InlineData("status", "--app")]
    [InlineData("logout", "--app")]
    [InlineData("token", "--mode")]
    [InlineData("scenario", "--session")]
    [InlineData("call", "--app")]
    [InlineData("login", "--password")]
    [InlineData("scenario", "--password")]
    public void AnOptionTheCommandDoesNotHave_IsAUsageError(string command, string option)
    {
        CliUsageException error = Assert.Throws<CliUsageException>(() => CliArguments.Parse([command, option, "value"]));
        Assert.Contains($"has no option '{option}'", error.Message);
    }

    [Fact]
    public void APasswordIsNeverAcceptedAsAnOption()
    {
        foreach (string command in CliArguments.Commands)
            Assert.Throws<CliUsageException>(() => CliArguments.Parse([command, "--password", "secret"]));
    }

    [Theory]
    [InlineData("--app")]
    [InlineData("--scheme")]
    [InlineData("--mode")]
    public void AnOptionWithoutAValue_IsAUsageError(string option)
    {
        CliUsageException error = Assert.Throws<CliUsageException>(() => CliArguments.Parse(["login", option]));
        Assert.Contains("needs a value", error.Message);
    }

    [Fact]
    public void AnOptionFollowedByAnotherOption_HasNoValue() =>
        Assert.Throws<CliUsageException>(() => CliArguments.Parse(["login", "--app", "--scheme", "blusky"]));

    [Fact]
    public void AnOptionGivenTwice_IsAUsageError()
    {
        CliUsageException error = Assert.Throws<CliUsageException>(() => CliArguments.Parse(["login", "--app", "a", "--app", "b"]));
        Assert.Contains("more than once", error.Message);
    }

    [Fact]
    public void AnOptionGivenTwiceInDifferentCase_IsStillGivenTwice() =>
        Assert.Throws<CliUsageException>(() => CliArguments.Parse(["login", "--app", "a", "--APP", "b"]));

    [Theory]
    [InlineData("--show-token")]
    [InlineData("--json")]
    public void ASwitch_IsOnWithoutAValue(string flag)
    {
        CliArguments arguments = CliArguments.Parse(["token", flag]);
        Assert.True(arguments.Flag(flag[2..]));
    }

    [Fact]
    public void ASwitchDoesNotSwallowTheNextArgument()
    {
        CliArguments arguments = CliArguments.Parse(["call", "--help", "api/profile"]);

        Assert.True(arguments.Flag("help"));
        Assert.Equal(["api/profile"], arguments.Positional);
    }

    [Theory]
    [InlineData("--json=true", true)]
    [InlineData("--json=false", false)]
    [InlineData("--json=TRUE", true)]
    public void ASwitchTakesAnExplicitTrueOrFalse(string argument, bool expected) =>
        Assert.Equal(expected, CliArguments.Parse(["status", argument]).Flag("json"));

    [Fact]
    public void ASwitchWithAnotherValue_IsAUsageError() =>
        Assert.Throws<CliUsageException>(() => CliArguments.Parse(["status", "--json=yes"]));

    [Fact]
    public void AnAbsentSwitch_IsOff() => Assert.False(CliArguments.Parse(["status"]).Flag("json"));

    [Theory]
    [InlineData("-h")]
    [InlineData("-?")]
    public void AShortHelpAfterACommand_AsksForThatCommandsHelp(string argument)
    {
        CliArguments arguments = CliArguments.Parse(["login", argument]);

        Assert.Equal("login", arguments.Command);
        Assert.True(arguments.Flag("help"));
    }

    [Fact]
    public void PositionalArguments_KeepTheirOrder()
    {
        CliArguments arguments = CliArguments.Parse(["call", "api/one", "--method", "GET"]);
        Assert.Equal(["api/one"], arguments.Positional);
    }

    [Fact]
    public void ADoubleDash_IsAPositionalArgument() =>
        Assert.Equal(["--"], CliArguments.Parse(["call", "--"]).Positional);

    [Fact]
    public void ABareDoubleDashWithEquals_IsNotAnOption() =>
        Assert.Throws<CliUsageException>(() => CliArguments.Parse(["login", "--=value"]));

    [Fact]
    public void Require_UsesTheOptionFirst()
    {
        CliArguments arguments = CliArguments.Parse(["login", "--app", "https://option.test"]);
        Assert.Equal("https://option.test", arguments.Require("app", _ => "https://variable.test", "CLOUDLOGIN_APP"));
    }

    [Fact]
    public void Require_FallsBackToTheEnvironment()
    {
        CliArguments arguments = CliArguments.Parse(["login"]);
        Assert.Equal("https://variable.test", arguments.Require("app", name => name == "CLOUDLOGIN_APP" ? "https://variable.test" : null, "CLOUDLOGIN_APP"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Require_WithNothingUsable_NamesTheOptionAndTheVariable(string? environmentValue)
    {
        CliUsageException error = Assert.Throws<CliUsageException>(() => CliArguments.Parse(["login"]).Require("app", _ => environmentValue, "CLOUDLOGIN_APP"));

        Assert.Contains("--app", error.Message);
        Assert.Contains("CLOUDLOGIN_APP", error.Message);
    }

    [Theory]
    [InlineData(null, 8)]
    [InlineData("1", 1)]
    [InlineData("64", 64)]
    [InlineData("16", 16)]
    public void Integer_ReadsWithinRange(string? value, int expected)
    {
        string[] args = value is null ? ["scenario"] : ["scenario", "--parallel", value];
        Assert.Equal(expected, CliArguments.Parse(args).Integer("parallel", 8, 1, 64));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65")]
    [InlineData("-1")]
    [InlineData("eight")]
    [InlineData("1.5")]
    [InlineData("")]
    public void Integer_OutOfRangeOrNotANumber_IsAUsageError(string value) =>
        Assert.Throws<CliUsageException>(() => CliArguments.Parse(["scenario", $"--parallel={value}"]).Integer("parallel", 8, 1, 64));

    [Fact]
    public void Get_ForAnOptionNotGiven_IsNull() => Assert.Null(CliArguments.Parse(["login"]).Get("app"));

    [Fact]
    public void Has_ReportsGivenOptions()
    {
        CliArguments arguments = CliArguments.Parse(["call", "x", "--body", "{}"]);

        Assert.True(arguments.Has("body"));
        Assert.False(arguments.Has("method"));
    }

    [Fact]
    public void Parse_RejectsNull() => Assert.Throws<ArgumentNullException>(() => CliArguments.Parse(null!));

    [Fact]
    public void EveryCommandAcceptsHelp()
    {
        foreach (string command in CliArguments.Commands.Where(command => command is not ("help" or "version")))
            Assert.True(CliArguments.Parse([command, "--help"]).Flag("help"));
    }
}
