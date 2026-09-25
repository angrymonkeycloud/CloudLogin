(() => {
  const storageKey = "cloudlogin-demo-theme";

  const resolveTheme = () => {
    const savedTheme = localStorage.getItem(storageKey);
    if (savedTheme === "light" || savedTheme === "dark") return savedTheme;
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  };

  const applyTheme = theme => {
    document.documentElement.dataset.theme = theme;
    document.documentElement.dataset.amcTheme = theme;
    document.querySelectorAll("[data-theme-label]").forEach(element => {
      element.textContent = theme === "dark" ? "Light mode" : "Dark mode";
    });
  };

  window.cloudLoginDemo = {
    initializeTheme: () => applyTheme(resolveTheme()),
    toggleTheme: () => {
      const theme = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
      localStorage.setItem(storageKey, theme);
      applyTheme(theme);
      return theme;
    },
    copyExample: async button => {
      const panel = button.closest("[data-panel]");
      try { await navigator.clipboard.writeText(panel.querySelector("code").textContent); panel.querySelector("[role=status]").textContent = "Code copied."; }
      catch { panel.querySelector("[role=status]").textContent = "Select and copy the code."; }
    },
    showExample: (button, tabName) => {
      const root = button.closest("[data-example]");
      if (!root) return;

      root.querySelectorAll("[role='tablist'] button").forEach(tabButton => {
        const selected = tabButton === button;
        tabButton.classList.toggle("active", selected);
        tabButton.tabIndex = selected ? 0 : -1;
        tabButton.setAttribute("aria-selected", selected.toString());
      });

      root.querySelectorAll("[data-panel]").forEach(panel => {
        panel.hidden = panel.dataset.panel !== tabName;
      });
    }
  };

  window.cloudLoginDemo.initializeTheme();
})();
