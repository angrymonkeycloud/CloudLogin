# CloudLogin developer workshop

The Embedded demo is the primary place to try features, copy code, and read integration guides. It uses the shipped components and local demo stores. The standalone authority and consumer demonstrate a separate-site authentication handoff.

| Demo | Start command from repository root | Open |
| --- | --- | --- |
| Full workshop | `dotnet run --project demo/CloudLogin.Demo.Embedded` | https://localhost:7300/workshop |
| Standalone authority | `dotnet run --project demo/CloudLogin.Demo` | https://localhost:7100/demo |
| Consumer | `dotnet run --project demo/CloudLogin.Demo.Consumer` | https://localhost:7200 |

Start the authority before the consumer. Use .NET 10 and the sibling source checkouts referenced by the projects: CloudComponents alongside CloudLogin, and CoconutSharp/CoconutSharpAspire beside the angrymonkeycloud directory. Trust the local ASP.NET development certificate for interactive authentication.

Every workshop example has **View**, **Code**, and **Instructions**. The left navigation searches features and reference guides. The right panel changes supported example options. Code can be copied with one button; tabs support arrow, Home, and End keys.

The full workshop covers password and email-code authentication, registration and recovery, profile/contact management, authenticator/passkey entry points, devices and sessions, sign-in history, administration, deletion/logout, workspace creation/membership/invitations, quota and role evaluation, configuration validation, redirect origins, and a captured verification inbox. Reference guides are embedded from the repository and are readable in Instructions without leaving the workshop.

Use Test mode and Demo Admin for a quick account tour. Request codes in Authentication and read them in Demo inbox. Workspaces calls the real account-registry services. Configuration evaluators use the production contracts but do not change other visitors' authentication policies. Demo state resets with its process or browser circuit as indicated in each example.

External OAuth, real message delivery, platform authenticators, production persistence, and multi-device session checks require the corresponding providers, devices, HTTPS, and credentials. The local workshop does not provision those services. Subscription and payment-processing routes from older documentation are not part of the current account-registry demo.

These applications refuse to start outside Development. Test accounts, captured codes, and the consumer's local-only client secret belong only to these hosts. Only designated live UI pages permit same-origin framing; the library's production frame protections remain unchanged.

## Build and validate

The compiled styles are included. The shared asset build updates all three hosts, including the authority inbox; edit the LESS and JavaScript sources rather than generated files. When editing LESS or JavaScript, rebuild them in `demo/CloudLogin.Demo.Embedded`:

```text
npm ci --ignore-scripts
npm run build:assets
```

Run all unit tests with coverage, build all three hosts, and audit workshop dependencies:

```text
pwsh ./scripts/validate.ps1
pwsh ./scripts/validate.ps1 -Browser
```

The browser run starts all three local hosts and checks every feature's tabs, clipboard, keyboard navigation, mobile width, live evaluators, framed authentication, workspace creation, the consumer-to-authority handoff, and safe inbox rendering. On Linux, install browser system libraries with `npx playwright install --with-deps chromium`. Reports are written under TestResults and the workshop's playwright-report directory.

GitHub Actions checks out the sibling sources into the required layout. If a sibling repository is private, configure the read-only WORKSPACE_READ_TOKEN secret; do not grant write permissions. The validation workflow does not deploy anything.

Older /login, /account, /workspaces, /providers, and /inbox links lead into the central workshop. The /playground routes are implementation hosts for live previews, rather than separate documentation surfaces.
