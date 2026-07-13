(function () {
  const storageKey = "biktal-theme";

  function apply(theme) {
    const resolved =
      theme === "dark" || theme === "light"
        ? theme
        : window.matchMedia("(prefers-color-scheme: dark)").matches
          ? "dark"
          : "light";

    document.documentElement.setAttribute("data-theme", resolved);
    document.documentElement.setAttribute("data-bs-theme", resolved);

    document.querySelectorAll("[data-theme-toggle]").forEach((btn) => {
      const label = resolved === "dark" ? "Switch to light mode" : "Switch to dark mode";
      btn.setAttribute("aria-label", label);
    });

    document.querySelectorAll("[data-theme-icon]").forEach((icon) => {
      icon.className =
        resolved === "dark" ? "bi bi-sun-fill" : "bi bi-moon-stars-fill";
    });
  }

  const stored = localStorage.getItem(storageKey);
  apply(stored || "system");

  window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => {
    const v = localStorage.getItem(storageKey);
    if (!v || v === "system") apply("system");
  });

  document.addEventListener("click", (e) => {
    const target = e.target.closest("[data-theme-toggle]");
    if (!target) return;

    const current = document.documentElement.getAttribute("data-theme") || "light";
    const next = current === "dark" ? "light" : "dark";
    localStorage.setItem(storageKey, next);
    apply(next);
  });
})();
