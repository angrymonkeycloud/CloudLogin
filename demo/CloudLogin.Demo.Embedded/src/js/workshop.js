window.workshop = {
  copy: async text => {
    if (!navigator.clipboard?.writeText) throw new Error("Clipboard unavailable");
    await navigator.clipboard.writeText(text);
  },
  focusTab: id => document.getElementById(id)?.focus()
};

document.addEventListener("click", async event => {
  const root = event.target.closest("[data-static-workshop]");
  if (!root) return;
  const tab = event.target.closest("[data-workshop-tab]");
  if (tab) {
    root.querySelectorAll("[data-workshop-tab]").forEach(item => {
      const active = item === tab;
      item.setAttribute("aria-selected", String(active));
      item.tabIndex = active ? 0 : -1;
      root.querySelector("#panel-" + item.dataset.workshopTab).hidden = !active;
    });
  }
  if (event.target.closest("[data-workshop-copy]")) {
    const panel = root.querySelector("#panel-Code");
    try { await window.workshop.copy(panel.querySelector("code").textContent); panel.querySelector("[role=status]").textContent = "Code copied."; }
    catch { panel.querySelector("[role=status]").textContent = "Select and copy the code."; }
  }
});
document.addEventListener("keydown", event => {
  const tab = event.target.closest("[data-static-workshop] [role=tab]");
  if (!tab) return;
  const tabs = [...tab.parentElement.querySelectorAll("[role=tab]")];
  const index = tabs.indexOf(tab);
  const next = event.key === "ArrowRight" ? (index + 1) % tabs.length : event.key === "ArrowLeft" ? (index + tabs.length - 1) % tabs.length : event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : -1;
  if (next < 0) return;
  event.preventDefault();
  tabs[next].click();
  tabs[next].focus();
});
