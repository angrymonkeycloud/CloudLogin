import { readFile, writeFile, mkdir, copyFile } from "node:fs/promises";
import path from "node:path";
import less from "less";
const root = process.cwd();
const styles = ["workshop"];
try { await readFile(path.join(root, "src/css/app.less")); styles.push("app"); } catch {}
for (const name of styles) {
  const source = path.join(root, "src/css", name + ".less");
  const output = path.join(root, "wwwroot/css", name + ".css");
  await mkdir(path.dirname(output), { recursive: true });
  const result = await less.render(await readFile(source, "utf8"), { filename: source, math: "always" });
  await writeFile(output, result.css);
}
await mkdir(path.join(root, "wwwroot/js"), { recursive: true });
await copyFile(path.join(root, "src/js/workshop.js"), path.join(root, "wwwroot/js/workshop.js"));
try { await copyFile(path.join(root, "src/js/app.js"), path.join(root, "wwwroot/js/app.js")); } catch {}

for (const demo of ["CloudLogin.Demo", "CloudLogin.Demo.Consumer"]) {
  for (const asset of ["css/workshop.css", "js/workshop.js"]) {
    const output = path.join(root, "..", demo, "wwwroot", asset);
    await mkdir(path.dirname(output), { recursive: true });
    await copyFile(path.join(root, "wwwroot", asset), output);
  }
}
const authorityRoot = path.join(root, "..", "CloudLogin.Demo");
const inboxSource = path.join(authorityRoot, "src/css/inbox.less");
const inboxStyles = await less.render(await readFile(inboxSource, "utf8"), { filename: inboxSource });
await writeFile(path.join(authorityRoot, "wwwroot/demo/css/inbox.css"), inboxStyles.css);
await copyFile(path.join(authorityRoot, "src/js/inbox.js"), path.join(authorityRoot, "wwwroot/demo/js/inbox.js"));