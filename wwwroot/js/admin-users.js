/**
 * Admin — styled delete-user confirmation modal (replaces window.confirm).
 */
(function () {
  const modal = document.getElementById("bk-admin-delete-modal");
  if (!modal) return;

  const nameEl = document.getElementById("bk-admin-delete-name");
  const emailEl = document.getElementById("bk-admin-delete-email");
  const confirmBtn = document.getElementById("bk-admin-delete-confirm");
  let pendingForm = null;
  let lastFocus = null;

  function openModal(form, displayName, email) {
    pendingForm = form;
    if (nameEl) nameEl.textContent = displayName || "User";
    if (emailEl) emailEl.textContent = email || "";
    modal.classList.remove("d-none");
    document.body.classList.add("bk-admin-modal-open");
    confirmBtn?.focus();
  }

  function closeModal() {
    modal.classList.add("d-none");
    document.body.classList.remove("bk-admin-modal-open");
    pendingForm = null;
    lastFocus?.focus();
    lastFocus = null;
  }

  document.querySelectorAll("[data-bk-delete-user]").forEach((btn) => {
    btn.addEventListener("click", (e) => {
      e.preventDefault();
      const form = btn.closest("form.bk-admin-delete-form");
      if (!form) return;
      lastFocus = btn;
      openModal(
        form,
        btn.getAttribute("data-delete-name") || "",
        btn.getAttribute("data-delete-email") || ""
      );
    });
  });

  confirmBtn?.addEventListener("click", () => {
    if (pendingForm) pendingForm.submit();
    closeModal();
  });

  modal.querySelectorAll("[data-bk-delete-dismiss]").forEach((el) => {
    el.addEventListener("click", closeModal);
  });

  document.addEventListener("keydown", (e) => {
    if (modal.classList.contains("d-none")) return;
    if (e.key === "Escape") {
      e.preventDefault();
      closeModal();
    }
  });
})();
