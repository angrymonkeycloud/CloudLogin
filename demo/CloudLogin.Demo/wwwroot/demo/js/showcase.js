(() => {
  const storageKey = "cloudlogin-authority-theme";

  const applyTheme = theme => {
    document.documentElement.dataset.theme = theme;
    document.querySelectorAll("[data-theme-label]").forEach(element => {
      element.textContent = theme === "dark" ? "Light mode" : "Dark mode";
    });
  };

  window.cloudLoginAuthority = {
    toggleTheme: () => {
      const nextTheme = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
      localStorage.setItem(storageKey, nextTheme);
      applyTheme(nextTheme);
    }
  };

  applyTheme(document.documentElement.dataset.theme || "light");
})();
