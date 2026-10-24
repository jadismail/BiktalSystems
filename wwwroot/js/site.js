(function () {
  // Normalize locale decimals (1,00 → 1.00) so HTML number inputs and model binding accept them.
  function normalizeDecimalInput(input) {
    if (!input || input.disabled || input.readOnly) return;
    var raw = input.value;
    if (!raw || raw.indexOf(",") < 0) return;
    // Treat comma as decimal separator when there is no period (e.g. 1,00 / 12,5).
    if (raw.indexOf(".") < 0) {
      input.value = raw.replace(",", ".");
      return;
    }
    // 1.234,56 → 1234.56
    input.value = raw.replace(/\./g, "").replace(",", ".");
  }

  function wireDecimalInputs(root) {
    var scope = root || document;
    scope.querySelectorAll('input[type="number"], input.bk-money-input, input[inputmode="decimal"]').forEach(function (input) {
      if (input.dataset.bkDecimalWired === "1") return;
      input.dataset.bkDecimalWired = "1";
      normalizeDecimalInput(input);
      input.addEventListener("blur", function () {
        normalizeDecimalInput(input);
      });
      var form = input.form;
      if (form && form.dataset.bkDecimalFormWired !== "1") {
        form.dataset.bkDecimalFormWired = "1";
        form.addEventListener("submit", function () {
          form.querySelectorAll('input[type="number"], input.bk-money-input, input[inputmode="decimal"]').forEach(normalizeDecimalInput);
        });
      }
    });
  }

  function patchJqueryStepAny() {
    if (!window.jQuery || !jQuery.validator || !jQuery.validator.methods || !jQuery.validator.methods.step)
      return;
    if (jQuery.validator.methods.step._bkAllowsAny) return;
    var originalStep = jQuery.validator.methods.step;
    jQuery.validator.methods.step = function (value, element, param) {
      if (String(param).toLowerCase() === "any")
        return this.optional(element) || true;
      return originalStep.call(this, value, element, param);
    };
    jQuery.validator.methods.step._bkAllowsAny = true;
  }

  function onReady() {
    wireDecimalInputs(document);
    patchJqueryStepAny();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", onReady);
  } else {
    onReady();
  }

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
