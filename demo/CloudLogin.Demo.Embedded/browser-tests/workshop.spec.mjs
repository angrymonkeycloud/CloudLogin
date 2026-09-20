import { test, expect } from "@playwright/test";

test("every feature has usable View, Code, and Instructions tabs", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/workshop");
  const links = await page.getByRole("navigation", { name: "Feature navigation" }).getByRole("link").evaluateAll(items => items.map(item => ({ href: item.getAttribute("href"), title: item.textContent })));
  expect(links.length).toBeGreaterThan(10);
  await page.screenshot({ path: "test-results/workshop-desktop.png", fullPage: true });
  for (const link of links) {
    await page.goto(link.href);
    await expect(page.locator("main h1")).toHaveText(link.title);
    await expect(page.getByRole("tab", { name: "View", exact: true })).toHaveAttribute("aria-selected", "true");
    await page.getByRole("tab", { name: "Code", exact: true }).click();
    await expect(page.locator("#panel-Code")).toBeVisible();
    await expect(page.locator("#panel-Code code")).not.toBeEmpty();
    await page.getByRole("tab", { name: "Instructions", exact: true }).click();
    await expect(page.locator("#panel-Instructions")).toBeVisible();
    await expect(page.locator("#panel-Instructions li").first()).toBeVisible();
    await expect(page.locator("#blazor-error-ui")).not.toBeVisible();
  }
  expect(errors).toEqual([]);
});

test("tabs support keyboard, clipboard, configuration and feature search", async ({ page, context }) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.goto("/workshop");
  await expect(page.locator(".workshop")).toHaveAttribute("aria-busy", "false");
  await page.getByRole("tab", { name: "View", exact: true }).focus();
  await page.keyboard.press("ArrowRight");
  await expect(page.getByRole("tab", { name: "Code", exact: true })).toBeFocused();
  await page.getByRole("button", { name: "Copy code", exact: true }).click();
  await expect(page.locator("#panel-Code")).toContainText("Code copied.");
  expect((await page.evaluate(() => navigator.clipboard.readText())).replaceAll("\r\n", "\n")).toBe((await page.locator("#panel-Code code").textContent()).replaceAll("\r\n", "\n"));
  await page.getByRole("tab", { name: "Code", exact: true }).press("End");
  await expect(page.getByRole("tab", { name: "Instructions", exact: true })).toBeFocused();
  await page.getByRole("button", { name: "Close configuration" }).click();
  await expect(page.locator("#workshop-configuration")).toHaveCount(0);
  await page.getByRole("button", { name: "Configure", exact: true }).click();
  await expect(page.locator("#workshop-configuration")).toBeVisible();
  await page.getByRole("searchbox").fill("no-feature-matches-this");
  await expect(page.getByText("No matching features.", { exact: true })).toBeVisible();
});

test("workshop fits a narrow screen without horizontal overflow", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/workshop");
  await expect(page.locator("main h1")).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
  await page.screenshot({ path: "test-results/workshop-mobile.png", fullPage: true });
});

test("live examples run without disconnecting the workshop", async ({ page }) => {
  await page.goto("/workshop");
  const links = await page.getByRole("navigation", { name: "Feature navigation" }).getByRole("link").evaluateAll(items => items.map(item => item.getAttribute("href")).filter(href => !href.includes("/guide-")));
  for (const href of links) {
    await page.goto(href);
    const preview = page.getByRole("button", { name: "Preview resource model", exact: true });
    if (await preview.count()) {
      await preview.click();
      await expect(page.getByRole("alert")).not.toBeVisible();
      await expect(page.locator("#panel-View tbody tr").first()).toBeVisible();
    }
    const run = page.getByRole("button", { name: /^(Run example|Validate and serialize|Preview configuration|Evaluate|Build preview)$/ });
    if (await run.count()) {
      await run.first().click();
      await expect(page.locator(".workshop-result").first()).not.toBeEmpty();
      await expect(page.locator(".workshop-result").first()).not.toContainText("Check the input:");
    }
    await page.getByRole("tab", { name: "Instructions", exact: true }).click();
    await expect(page.locator("#panel-Instructions")).toBeVisible();
  }
});

test("live authentication and workspace previews load inside their frames", async ({ page, request }) => {
  const response = await request.get("/playground/login");
  expect(response.headers()["x-frame-options"]).toBe("SAMEORIGIN");
  expect(response.headers()["content-security-policy"]).toContain("frame-ancestors 'self'");
  const protectedResponse = await request.get("/CloudLogin/does-not-exist");
  expect(protectedResponse.headers()["x-frame-options"]).toBe("DENY");
  await page.goto("/workshop/authentication");
  const login = page.frameLocator("iframe");
  await expect(login.locator("body")).toContainText(/Test mode|Email and password|Sign in|Log in/, { timeout: 30000 });
  await expect(login.locator("#blazor-error-ui")).not.toBeVisible();
  await page.screenshot({ path: "test-results/cloudlogin-live.png", fullPage: true });
  await page.goto("/workshop/workspaces");
  await expect(page.locator(".workshop")).toHaveAttribute("aria-busy", "false");
  const workspace = page.frameLocator("iframe");
  await workspace.getByPlaceholder("Workspace name").fill("Browser workshop");
  await workspace.getByPlaceholder("Workspace name").press("Tab");
  await workspace.getByRole("button", { name: "Create", exact: true }).click();
  await expect(workspace.getByRole("heading", { name: "Browser workshop", exact: true })).toBeVisible();
});
