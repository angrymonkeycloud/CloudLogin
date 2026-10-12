namespace AngryMonkey.CloudLogin.Cli;

internal static class CliHelp
{
    private const string Common = """
          --app <url>         The application's backend, for example https://app.example.com (or CLOUDLOGIN_APP).
          --scheme <scheme>   The callback scheme the native application registers, for example blusky (or CLOUDLOGIN_SCHEME).
        """;

    private const string SignIn = """
          --mode <mode>       How to sign in at CloudLogin (or CLOUDLOGIN_MODE):
                                paste     sign in in your own browser and paste the address it ends on (any deployment);
                                testmode  sign in through Test Mode, which local and test deployments enable;
                                password  sign in with an email address and password.
          --test-user <name>  Test Mode user to sign in as (name, email or id). Defaults to the first test user.
          --create-test-user  Create the Test Mode user when none matches.
          --email <address>   Email address for password sign-in (or CLOUDLOGIN_EMAIL). The password comes from
                              CLOUDLOGIN_PASSWORD or is asked for; it is never taken as an option.
        """;

    private const string Session = """
          --session <file>    Where the session is kept (or CLOUDLOGIN_SESSION). It is encrypted for the current user.
        """;

    public static string For(string command) => command switch
    {
        "login" => $"""
            cloudlogin login --app <url> --scheme <scheme> [options]

            Signs in exactly as the native application does: a PKCE verifier and state, the system browser sign-in through the
            application's backend, and the one-time handoff redeemed for the application's session, which is then saved.

            {Common}
            {SignIn}
            {Session}
              --no-open           With --mode paste, print the address instead of opening a browser.
            """,
        "status" => $"""
            cloudlogin status [--audience <audience>] [--json]

            Checks that the saved session is still valid, renewing it on the way as the application would.
            Exit code 0 when it is valid, 1 when it has ended, 3 when it could not be checked.

            {Session}
            """,
        "token" => $"""
            cloudlogin token [--audience <audience>] [--show-token] [--json]

            Gets an access token through the saved session and prints its claims. The token itself is printed only with --show-token.

            {Session}
            """,
        "call" => $"""
            cloudlogin call <path> [--method <method>] [--body <json>]

            Calls a path on the application with the saved session, as the application's own requests do.

            {Session}
            """,
        "logout" => $"""
            cloudlogin logout

            Ends the session with no browser: the application ends it at CloudLogin for every application that shares it.
            Exit code 3 means it could not be reached and nothing changed; run it again.

            {Session}
            """,
        "scenario" => $"""
            cloudlogin scenario --app <url> --scheme <scheme> --mode testmode|password [options]

            Runs a native session's whole life against a live deployment and checks every step: sign-in, the state and the
            single-use handoff, the PKCE verifier, renewal under concurrent requests, an older cookie, a web page trying to end
            the session, logout, the ended cookie, and that the next sign-in asks again. Exit code 0 only when every step passes.

            {Common}
            {SignIn}
              --parallel <n>      Requests sent at the same moment in the renewal step (default 8).
              --json              Print the result as JSON.
            """,
        _ => """
            cloudlogin <command> [options]

            Commands:
              login      Sign in as the native application does and save the session.
              status     Check that the saved session is still valid.
              token      Get an access token through the saved session and show its claims.
              call       Call a path on the application with the saved session.
              logout     End the session everywhere, with no browser.
              scenario   Check sign-in, renewal and logout end to end against a deployment.
              version    Show the version.

            Run 'cloudlogin <command> --help' for a command's options.

            Exit codes: 0 success, 1 failed, 2 wrong usage, 3 the server could not be reached (try again).
            """
    };
}
