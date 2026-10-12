# CloudLogin CLI

The `cloudlogin` command signs in to a CloudLogin application from the command line exactly as its native (MAUI) app does, and checks
every step of a native session against a running deployment: sign-in, session renewal and logout.

## Install

```bash
dotnet tool install --global AngryMonkey.CloudLogin.Cli
```

## Sign in, check, and sign out

```bash
cloudlogin login --app https://app.example.com --scheme example
cloudlogin status
cloudlogin token --audience portal
cloudlogin call api/profile
cloudlogin logout
```

`login` uses `--mode paste` by default: it prints the sign-in address, you sign in in your own browser, and you paste the address the
browser ends on (the application's own scheme, which no web page answers). This works with any provider, against any deployment.

For a local or test deployment that enables Test Mode, sign-in needs no browser at all:

```bash
cloudlogin login --app https://localhost:7074 --scheme example --mode testmode --test-user "Ada Lovelace"
```

The session is saved encrypted for the current user (`--session` chooses the file). Passwords are never taken as options: set
`CLOUDLOGIN_PASSWORD` or type it when asked.

## Check a deployment end to end

```bash
cloudlogin scenario --app https://localhost:7074 --scheme example --mode testmode --create-test-user
```

The scenario signs in, renews the session with many requests at the same moment, replays an older cookie, tries to end the session from
another site, signs out, and confirms that the ended session is refused everywhere and that the next sign-in asks again. It exits with 0
only when every step passes, so it can gate a deployment.

| Exit code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | Failed, or the session has ended |
| 2 | Wrong usage |
| 3 | The server could not be reached; nothing changed, try again |

This project is part of [Angry Monkey Cloud](https://angrymonkeycloud.com). AI agents working in it follow the shared
[instructions](https://github.com/angrymonkeycloud/CloudDocs/blob/main/docs/ai/instructions.md).
