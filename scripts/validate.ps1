[CmdletBinding()]
param([switch]$Browser)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repository = Split-Path $PSScriptRoot -Parent
Push-Location $repository
try {
    dotnet test CloudLogin.Tests/CloudLogin.Tests.csproj --configuration Release --logger trx --collect 'XPlat Code Coverage' --results-directory TestResults/CloudLogin.Tests
    if ($LASTEXITCODE -ne 0) { throw 'CloudLogin tests failed.' }
    Push-Location demo/CloudLogin.Demo.Embedded
    try {
        npm ci --ignore-scripts --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Workshop dependency installation failed.' }
        npm run build:assets
        if ($LASTEXITCODE -ne 0) { throw 'Workshop asset build failed.' }
        foreach ($project in @('CloudLogin.Demo.Embedded.csproj', '../CloudLogin.Demo/CloudLogin.Demo.csproj', '../CloudLogin.Demo.Consumer/CloudLogin.Demo.Consumer.csproj')) {
            dotnet build $project --configuration Release
            if ($LASTEXITCODE -ne 0) { throw "Demo build failed: $project" }
        }
        npm audit --audit-level=high
        if ($LASTEXITCODE -ne 0) { throw 'Workshop dependencies require security review.' }
        if ($Browser) {
            dotnet dev-certs https
            if ($LASTEXITCODE -ne 0) { throw 'Local HTTPS certificate setup failed.' }
            npx playwright install chromium
            if ($LASTEXITCODE -ne 0) { throw 'Browser installation failed.' }
            npm run test:browser
            if ($LASTEXITCODE -ne 0) { throw 'Workshop browser tests failed.' }
        }
    }
    finally { Pop-Location }
}
finally { Pop-Location }
