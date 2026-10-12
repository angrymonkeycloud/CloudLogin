namespace AngryMonkey.CloudLogin.Cli;

public sealed class CliUsageException(string message) : Exception(message);

/// <summary>A command, its positional arguments, and its options. Unknown options are an error rather than silently ignored.</summary>
public sealed class CliArguments
{
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "show-token", "no-open", "json", "create-test-user", "keep-session", "help" };

    private static readonly Dictionary<string, HashSet<string>> KnownOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["login"] = ["app", "scheme", "mode", "test-user", "create-test-user", "email", "session", "no-open", "help"],
        ["status"] = ["session", "audience", "json", "help"],
        ["token"] = ["session", "audience", "show-token", "json", "help"],
        ["call"] = ["session", "method", "body", "help"],
        ["logout"] = ["session", "help"],
        ["scenario"] = ["app", "scheme", "mode", "test-user", "create-test-user", "email", "parallel", "json", "help"],
        ["help"] = [],
        ["version"] = []
    };

    private readonly Dictionary<string, string> _options;

    private CliArguments(string command, IReadOnlyList<string> positional, Dictionary<string, string> options)
    {
        Command = command;
        Positional = positional;
        _options = options;
    }

    public string Command { get; }
    public IReadOnlyList<string> Positional { get; }
    public IReadOnlyCollection<string> OptionNames => _options.Keys;

    public static IReadOnlyCollection<string> Commands => KnownOptions.Keys;

    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0 || args[0] is "-h" or "--help" or "-?" or "/?")
            return new CliArguments("help", [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        if (args[0] is "--version" or "-v")
            return new CliArguments("version", [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        string command = args[0].ToLowerInvariant();

        if (!KnownOptions.TryGetValue(command, out HashSet<string>? allowed))
            throw new CliUsageException($"Unknown command '{args[0]}'. Run 'cloudlogin help' to see the commands.");

        List<string> positional = [];
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];

            if (argument is "-h" or "-?")
                argument = "--help";

            if (!argument.StartsWith("--", StringComparison.Ordinal) || argument.Length == 2)
            {
                positional.Add(argument);
                continue;
            }

            string name = argument[2..];
            string? value = null;
            int separator = name.IndexOf('=');

            if (separator >= 0)
            {
                value = name[(separator + 1)..];
                name = name[..separator];
            }

            name = name.ToLowerInvariant();

            if (name.Length == 0)
                throw new CliUsageException($"'{argument}' is not an option.");

            if (!allowed.Contains(name))
                throw new CliUsageException($"'{command}' has no option '--{name}'. Run 'cloudlogin {command} --help'.");

            if (Flags.Contains(name))
            {
                if (value is not null && !bool.TryParse(value, out _))
                    throw new CliUsageException($"'--{name}' is a switch; it takes true or false, not '{value}'.");

                value ??= "true";
            }
            else if (value is null)
            {
                if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new CliUsageException($"'--{name}' needs a value.");

                value = args[++index];
            }

            if (!options.TryAdd(name, value))
                throw new CliUsageException($"'--{name}' is given more than once.");
        }

        return new CliArguments(command, positional, options);
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Get(string name) => _options.TryGetValue(name, out string? value) ? value : null;

    public bool Flag(string name) => _options.TryGetValue(name, out string? value) && bool.TryParse(value, out bool on) && on;

    public string Require(string name, Func<string, string?> environment, string environmentVariable)
    {
        string? value = Get(name) ?? environment(environmentVariable);

        if (string.IsNullOrWhiteSpace(value))
            throw new CliUsageException($"'--{name}' is required (or set {environmentVariable}).");

        return value;
    }

    public int Integer(string name, int fallback, int minimum, int maximum)
    {
        string? raw = Get(name);

        if (raw is null)
            return fallback;

        if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value) || value < minimum || value > maximum)
            throw new CliUsageException($"'--{name}' must be a whole number from {minimum} to {maximum}.");

        return value;
    }
}
