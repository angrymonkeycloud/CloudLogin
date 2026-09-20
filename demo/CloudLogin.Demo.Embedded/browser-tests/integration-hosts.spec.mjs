import { test, expect } from "@playwright/test";

test("standalone authority uses the same workshop tabs and loads its real login form", async ({ page }) => {
  await page.goto("https://localhost:7100/demo");
  await expect(page.locator(".workshop")).toHaveAttribute("aria-busy", "false");
  await expect(page.locator(".workshop-nav")).toHaveCSS("position", "sticky");
  await expect(page.frameLocator("iframe").locator("body")).toContainText(/Test mode|Sign in/, { timeout: 30000 });
  const links = await page.getByRole("navigation", { name: "Feature navigation" }).getByRole("link").evaluateAll(items => items.map(item => item.href));
  expect(links).toHaveLength(4);
  for (const link of links) {
    await page.goto(link);
    await page.getByRole("tab", { name: "Code", exact: true }).click();
    await expect(page.locator("#panel-Code code")).not.toBeEmpty();
    await page.getByRole("tab", { name: "Instructions", exact: true }).click();
    await expect(page.locator("#panel-Instructions")).toBeVisible();
  }
});

test("consumer exposes copyable integration instructions and hands login to the authority", async ({ page }) => {
  await page.goto("https://localhost:7200/");
  await expect(page.locator(".workshop-nav")).toHaveCSS("position", "sticky");
  await page.getByRole("tab", { name: "Code", exact: true }).click();
  await expect(page.locator("#panel-Code")).toContainText("AddCloudLoginTokenAuthentication");
  await page.getByRole("tab", { name: "Instructions", exact: true }).click();
  await expect(page.locator("#panel-Instructions")).toBeVisible();
  await page.getByRole("tab", { name: "View", exact: true }).click();
  await page.getByRole("link", { name: "Log in via CloudLogin" }).click();
  await expect(page).toHaveURL(/^https:\/\/localhost:7100\//);
});

test("authority inbox renders message values as text instead of executable markup", async ({ page }) => {
  const address = '<img src=x onerror="window.inboxInjection=true">';
  const code = '<script>window.inboxInjection=true</script>';
  await page.route("**/demo-api/inbox", route => route.fulfill({
    contentType: "application/json",
    body: JSON.stringify([{ address, code, sentAt: "2026-09-19T12:00:00Z" }])
  }));
  await page.goto("https://localhost:7100/demo/inbox.html");
  await expect(page.locator("#entries td").nth(0)).toHaveText(address);
  await expect(page.locator("#entries code")).toHaveText(code);
  await expect(page.locator("#entries img, #entries script")).toHaveCount(0);
  expect(await page.evaluate(() => window.inboxInjection)).toBeUndefined();
});