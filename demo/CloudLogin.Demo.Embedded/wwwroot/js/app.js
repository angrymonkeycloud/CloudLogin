(() => {
  const storageKey = "cloudlogin-demo-theme";

  const resolveTheme = () => {
    const savedTheme = localStorage.getItem(storageKey);
    if (savedTheme === "light" || savedTheme === "dark") return savedTheme;
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  };

  const applyTheme = theme => {
    document.documentElement.dataset.theme = theme;
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
    showExample: (button, tabName) => {
      const root = button.closest("[data-example]");
      if (!root) return;

      root.querySelectorAll("[role='tablist'] button").forEach(tabButton => {
        const selected = tabButton === button;
        tabButton.classList.toggle("active", selected);
        tabButton.setAttribute("aria-selected", selected.toString());
      });

      root.querySelectorAll("[data-panel]").forEach(panel => {
        panel.hidden = panel.dataset.panel !== tabName;
      });
    }
  };

  window.cloudLoginDemo.initializeTheme();
})();
