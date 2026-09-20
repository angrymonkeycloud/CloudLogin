import { defineConfig } from "@playwright/test";

const baseURL = process.env.WORKSHOP_URL || "http://127.0.0.1:7301";
export default defineConfig({
  testDir: "./browser-tests",
  fullyParallel: false,
  timeout: 90000,
  use: {
    baseURL,
    ignoreHTTPSErrors: true,
    viewport: { width: 1440, height: 1000 },
    launchOptions: process.env.WORKSHOP_BROWSER ? { executablePath: process.env.WORKSHOP_BROWSER } : {},
    screenshot: "only-on-failure",
    trace: "retain-on-failure"
  },
  webServer: process.env.WORKSHOP_EXTERNAL ? undefined : [{
    command: "dotnet run --configuration Release --no-build --no-launch-profile --urls http://127.0.0.1:7301",
    url: baseURL + "/workshop",
    reuseExistingServer: !process.env.CI,
    env: { ASPNETCORE_ENVIRONMENT: "Development" },
    timeout: 120000
  }, {
    command: "dotnet run --project ../CloudLogin.Demo/CloudLogin.Demo.csproj --configuration Release --no-build --no-launch-profile --urls https://localhost:7100",
    url: "https://localhost:7100/demo",
    reuseExistingServer: !process.env.CI,
    ignoreHTTPSErrors: true,
    env: { ASPNETCORE_ENVIRONMENT: "Development" },
    timeout: 120000
  }, {
    command: "dotnet run --project ../CloudLogin.Demo.Consumer/CloudLogin.Demo.Consumer.csproj --configuration Release --no-build --no-launch-profile --urls https://localhost:7200",
    url: "https://localhost:7200/",
    reuseExistingServer: !process.env.CI,
    ignoreHTTPSErrors: true,
    env: { ASPNETCORE_ENVIRONMENT: "Development" },
    timeout: 120000
  }],
  reporter: [["list"], ["html", { open: "never" }]]
});
