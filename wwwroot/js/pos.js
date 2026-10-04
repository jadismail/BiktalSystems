/**
 * Biktal POS — Tech-store style register (MVC). Catalog from server JSON; cart & tendering in-browser.
 */
(function () {
  const root = document.getElementById("bk-pos-root");
  const catalogEl = document.getElementById("bk-pos-catalog-json");
  if (!root || !catalogEl) return;

  const taxRateDec = parseFloat(root.dataset.taxRate || "0") || 0;
  const taxPctLabel = root.dataset.taxPercent || String(taxRateDec * 100);
  const storeName = root.dataset.storeName || "Khulasa Retail";
  const receiptFooter =
    root.dataset.receiptFooter?.trim() ||
    "Keep this receipt for returns and warranty.";
  const lbpPerUsd = parseFloat(root.dataset.lbpPerUsd || "89500") || 89500;
  const completeSaleUrl = root.dataset.completeUrl || "";
  const antiforgeryToken = root.dataset.antiforgery || "";

  /** @type {{ id: string, name: string, sku: string, barcode?: string | null, price: number, category: string, stock: number }[]} */
  let catalog = [];
  try {
    catalog = JSON.parse(catalogEl.textContent || "[]");
  } catch {
    catalog = [];
  }

  /** @type {{ id: string, name: string, email: string, phone: string }[]} */
  let customers = [];
  try {
    const customersEl = document.getElementById("bk-pos-customers-json");
    customers = JSON.parse(customersEl?.textContent || "[]");
    if (!Array.isArray(customers)) customers = [];
  } catch {
    customers = [];
  }

  /** @type {{ id: string, name: string, email: string, phone: string } | null} */
  let selectedCustomer = null;
  /** @type {Map<string, { product: (typeof catalog)[0], qty: number }>} */
  let cart = new Map();

  const PAYMENT_METHODS = ["Cash", "Whish", "Debit"];

  /** @type {{ Cash: number, Whish: number, Debit: number }} */
  let paymentSplits = { Cash: 0, Whish: 0, Debit: 0 };
  let cashTendered = 0;
  /** User-entered discount in dollars; capped to subtotal when calculating totals. */
  let discountAmount = 0;
  let issueInvoice = false;
  /** @type {"receipt" | "invoice"} */
  let activeDocView = "receipt";

  const currencyCode = root.dataset.currency || "USD";
  const money = new Intl.NumberFormat(undefined, { style: "currency", currency: currencyCode });
  const moneyPlain = (n) => (Math.round(n * 100) / 100).toFixed(2);

  const els = {
    taxPct: document.getElementById("bk-pos-tax-pct"),
    subtotal: document.getElementById("bk-pos-subtotal"),
    discount: document.getElementById("bk-pos-discount"),
    tax: document.getElementById("bk-pos-tax"),
    total: document.getElementById("bk-pos-total"),
    customerSearch: document.getElementById("bk-pos-customer-search"),
    customerDd: document.getElementById("bk-pos-customer-dd"),
    customerChip: document.getElementById("bk-pos-customer-chip"),
    customerChipName: document.getElementById("bk-pos-customer-chip-name"),
    customerClear: document.getElementById("bk-pos-customer-clear"),
    customerAdd: document.getElementById("bk-pos-customer-add"),
    productSearch: document.getElementById("bk-pos-product-search"),
    productDd: document.getElementById("bk-pos-product-dd"),
    cartLines: document.getElementById("bk-pos-cart-lines"),
    cartClear: document.getElementById("bk-pos-cart-clear"),
    payCash: document.getElementById("bk-pos-pay-cash"),
    payWhish: document.getElementById("bk-pos-pay-whish"),
    payDebit: document.getElementById("bk-pos-pay-debit"),
    payPaid: document.getElementById("bk-pos-pay-paid"),
    payRemaining: document.getElementById("bk-pos-pay-remaining"),
    payRemainingWrap: document.getElementById("bk-pos-pay-remaining-wrap"),
    cashTenderedWrap: document.getElementById("bk-pos-cash-tendered-wrap"),
    cashTenderedInput: document.getElementById("bk-pos-cash-tendered"),
    changeWrap: document.getElementById("bk-pos-change-wrap"),
    changeLabel: document.getElementById("bk-pos-change-label"),
    change: document.getElementById("bk-pos-change"),
    lbpWrap: document.getElementById("bk-pos-lbp-wrap"),
    lbpRateLabel: document.getElementById("bk-pos-lbp-rate-label"),
    lbpTotalChange: document.getElementById("bk-pos-lbp-total-change"),
    lbpUsd: document.getElementById("bk-pos-lbp-usd"),
    lbpRemain: document.getElementById("bk-pos-lbp-remain"),
    complete: document.getElementById("bk-pos-complete"),
    print: document.getElementById("bk-pos-print"),
    promo: document.getElementById("bk-pos-promo"),
    promoApply: document.getElementById("bk-pos-promo-apply"),
    scanOpen: document.getElementById("bk-pos-scan-open"),
    modalScan: document.getElementById("bk-pos-modal-scan"),
    modalCustomer: document.getElementById("bk-pos-modal-customer"),
    newCustName: document.getElementById("bk-pos-new-cust-name"),
    newCustEmail: document.getElementById("bk-pos-new-cust-email"),
    newCustPhone: document.getElementById("bk-pos-new-cust-phone"),
    newCustSave: document.getElementById("bk-pos-new-cust-save"),
    toast: document.getElementById("bk-pos-toast"),
    toastMsg: document.getElementById("bk-pos-toast-msg"),
    toastIcon: document.getElementById("bk-pos-toast-icon"),
    toastClose: document.getElementById("bk-pos-toast-close"),
    modalReceipt: document.getElementById("bk-pos-modal-receipt"),
    receiptBody: document.getElementById("bk-pos-receipt-body"),
    receiptPrint: document.getElementById("bk-pos-receipt-print"),
    invoicePrint: document.getElementById("bk-pos-invoice-print"),
    issueInvoice: document.getElementById("bk-pos-issue-invoice"),
    saleDocPanel: document.getElementById("bk-pos-sale-doc-panel"),
    receiptPrintArea: document.getElementById("bk-pos-receipt-print-area"),
    sessionsBar: document.getElementById("bk-pos-sessions"),
    newSession: document.getElementById("bk-pos-new-session"),
  };

  /** @type {object | null} */
  let lastReceiptSnapshot = null;
  /** Receipt shown in modal — kept separate so session switches don't lose it. */
  let pendingReceiptSnapshot = null;

  /** @type {{ id: string, label: string, selectedCustomer: typeof selectedCustomer, cartEntries: { productId: string, qty: number }[], paymentMethod: string, discountAmount: number, amountReceived: number, lastReceiptSnapshot: object | null, ui: Record<string, string> }[]} */
  let sessions = [];
  let activeSessionId = null;
  let sessionCounter = 0;

  function getActiveSession() {
    return sessions.find((s) => s.id === activeSessionId) || null;
  }

  function serializeCart() {
    return [...cart.entries()].map(([productId, { qty }]) => ({ productId, qty }));
  }

  function deserializeCart(entries) {
    const m = new Map();
    for (const { productId, qty } of entries || []) {
      const p = catalog.find((x) => x.id === productId);
      if (p && qty > 0) m.set(productId, { product: p, qty });
    }
    return m;
  }

  function flushSessionState(session) {
    if (!session) return;
    session.selectedCustomer = selectedCustomer;
    session.cartEntries = serializeCart();
    session.paymentSplits = { ...paymentSplits };
    session.cashTendered = cashTendered;
    session.discountAmount = discountAmount;
    session.issueInvoice = issueInvoice;
    session.lastReceiptSnapshot = lastReceiptSnapshot;
    session.ui = {
      customerSearch: els.customerSearch?.value || "",
      productSearch: els.productSearch?.value || "",
      promoValue: els.promo?.value || "",
      lbpUsd: els.lbpUsd?.value || "",
      payCash: els.payCash?.value || "",
      payWhish: els.payWhish?.value || "",
      payDebit: els.payDebit?.value || "",
      cashTendered: els.cashTenderedInput?.value || "",
    };
  }

  function sessionTabTitle(session) {
    if (session.selectedCustomer?.name) return session.selectedCustomer.name;
    return session.label;
  }

  function sessionCartCount(session) {
    return (session.cartEntries || []).reduce((n, e) => n + e.qty, 0);
  }

  function loadSessionState(session) {
    selectedCustomer = session.selectedCustomer || null;
    cart = deserializeCart(session.cartEntries);
    const savedSplits = session.paymentSplits || {};
    paymentSplits = {
      Cash: Number(savedSplits.Cash || 0) || 0,
      Whish: Number(savedSplits.Whish || 0) || 0,
      Debit: Number(savedSplits.Debit || savedSplits.Card || 0) || 0,
    };
    cashTendered = session.cashTendered || 0;
    discountAmount = session.discountAmount || 0;
    issueInvoice = !!session.issueInvoice;
    if (els.issueInvoice) els.issueInvoice.checked = issueInvoice;
    lastReceiptSnapshot = session.lastReceiptSnapshot || null;

    const ui = session.ui || {};
    if (els.customerSearch) els.customerSearch.value = ui.customerSearch || "";
    if (els.productSearch) els.productSearch.value = ui.productSearch || "";
    if (els.promo) els.promo.value = ui.promoValue || "";
    if (els.lbpUsd) els.lbpUsd.value = ui.lbpUsd || "";
    writePaymentSplitsToInputs();
    if (ui.payCash !== undefined && els.payCash) els.payCash.value = ui.payCash;
    if (ui.payWhish !== undefined && els.payWhish) els.payWhish.value = ui.payWhish;
    if (ui.payDebit !== undefined && els.payDebit) els.payDebit.value = ui.payDebit;
    else if (ui.payCard !== undefined && els.payDebit) els.payDebit.value = ui.payCard;
    if (ui.cashTendered !== undefined && els.cashTenderedInput) els.cashTenderedInput.value = ui.cashTendered;

    if (selectedCustomer && els.customerChipName && els.customerChip) {
      els.customerChipName.textContent = selectedCustomer.name;
      els.customerChip.classList.remove("d-none");
    } else if (els.customerChip) {
      els.customerChip.classList.add("d-none");
    }

    hideCustomerDd();
    hideProductDd();
    renderCart();
  }

  function renderSessionTabs() {
    if (!els.sessionsBar) return;
    flushSessionState(getActiveSession());
    const canClose = sessions.length > 1;

    els.sessionsBar.innerHTML = sessions
      .map((session) => {
        const isActive = session.id === activeSessionId;
        const title = escapeHtml(sessionTabTitle(session));
        const count = sessionCartCount(session);
        const icon = session.selectedCustomer ? "bi-person-fill" : "bi-bag";
        const meta =
          count > 0
            ? `<span class="bk-pos-v2-session-tab-meta">${count} item${count === 1 ? "" : "s"}</span>`
            : `<span class="bk-pos-v2-session-tab-meta">Empty</span>`;
        const closeBtn = canClose
          ? `<button type="button" class="bk-pos-v2-session-tab-close" data-session-close="${escapeAttr(session.id)}" aria-label="Close ${title}"><i class="bi bi-x-lg" aria-hidden="true"></i></button>`
          : "";
        return `<div class="bk-pos-v2-session-tab${isActive ? " is-active" : ""}"
            role="tab"
            tabindex="${isActive ? "0" : "-1"}"
            aria-selected="${isActive}"
            data-session-id="${escapeAttr(session.id)}">
            <span class="bk-pos-v2-session-tab-icon" aria-hidden="true"><i class="bi ${icon}"></i></span>
            <span class="bk-pos-v2-session-tab-body">
              <span class="bk-pos-v2-session-tab-label">${title}</span>
              ${meta}
            </span>
            ${closeBtn}
          </div>`;
      })
      .join("");
  }

  function switchSession(id) {
    if (id === activeSessionId) return;
    const target = sessions.find((s) => s.id === id);
    if (!target) return;
    flushSessionState(getActiveSession());
    activeSessionId = id;
    loadSessionState(target);
    renderSessionTabs();
    if (els.sessionsBar) {
      const activeTab = [...els.sessionsBar.querySelectorAll("[data-session-id]")].find(
        (el) => el.getAttribute("data-session-id") === id
      );
      activeTab?.focus();
    }
  }

  function createNewSession() {
    flushSessionState(getActiveSession());
    const session = createEmptySession();
    sessions.push(session);
    activeSessionId = session.id;
    loadSessionState(session);
    renderSessionTabs();
    showToast(session.label + " started");
    focusProductSearch();
  }

  function normalizeSingleSession() {
    if (sessions.length !== 1) return;
    sessionCounter = 1;
    sessions[0].label = "Session 1";
  }

  function createEmptySession() {
    sessionCounter += 1;
    return {
      id: "sess-" + Date.now() + "-" + Math.random().toString(36).slice(2, 6),
      label: "Session " + sessionCounter,
      selectedCustomer: null,
      cartEntries: [],
      paymentSplits: { Cash: 0, Whish: 0, Debit: 0 },
      cashTendered: 0,
      discountAmount: 0,
      issueInvoice: false,
      lastReceiptSnapshot: null,
      ui: {},
    };
  }

  function closeSession(id) {
    if (sessions.length <= 1) return;
    const idx = sessions.findIndex((s) => s.id === id);
    if (idx < 0) return;

    flushSessionState(getActiveSession());
    const closingActive = id === activeSessionId;
    sessions.splice(idx, 1);

    if (closingActive) {
      const next = sessions[Math.min(idx, sessions.length - 1)];
      activeSessionId = next.id;
      loadSessionState(next);
    }

    normalizeSingleSession();
    renderSessionTabs();
  }

  function removeSessionAfterSale() {
    const id = activeSessionId;
    const idx = sessions.findIndex((s) => s.id === id);
    if (idx < 0) return;

    sessions.splice(idx, 1);

    if (sessions.length === 0) {
      sessionCounter = 0;
      const session = createEmptySession();
      sessions.push(session);
      activeSessionId = session.id;
      loadSessionState(session);
    } else {
      const next = sessions[Math.min(idx, sessions.length - 1)];
      activeSessionId = next.id;
      loadSessionState(next);
      normalizeSingleSession();
    }

    renderSessionTabs();
  }

  function initSessions() {
    sessionCounter = 0;
    const initial = createEmptySession();
    sessions = [initial];
    activeSessionId = initial.id;
    renderSessionTabs();
  }

  if (els.taxPct) els.taxPct.textContent = taxPctLabel;

  const lbpNumberFmt = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });

  function formatLbp(amount) {
    return `${lbpNumberFmt.format(Math.round(Math.max(0, amount)))} LBP`;
  }

  if (els.lbpRateLabel) {
    els.lbpRateLabel.textContent = `1 USD = ${lbpNumberFmt.format(lbpPerUsd)} LBP`;
  }

  function escapeHtml(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function escapeAttr(s) {
    return escapeHtml(s).replace(/'/g, "&#39;");
  }

  function subtotal() {
    let s = 0;
    for (const { product: p, qty } of cart.values()) s += p.price * qty;
    return s;
  }

  function effectiveDiscount() {
    return Math.min(discountAmount, subtotal());
  }

  function taxable() {
    return Math.max(0, subtotal() - effectiveDiscount());
  }

  function taxAmt() {
    return taxable() * taxRateDec;
  }

  function total() {
    return taxable() + taxAmt();
  }

  const payInputs = {
    Cash: () => els.payCash,
    Whish: () => els.payWhish,
    Debit: () => els.payDebit,
  };

  function readPaymentSplitsFromInputs() {
    for (const method of PAYMENT_METHODS) {
      const input = payInputs[method]?.();
      const val = parseFloat(input?.value);
      paymentSplits[method] = Number.isFinite(val) && val > 0 ? val : 0;
    }
    const tendered = parseFloat(els.cashTenderedInput?.value);
    cashTendered = Number.isFinite(tendered) && tendered > 0 ? tendered : 0;
  }

  function writePaymentSplitsToInputs() {
    if (els.payCash) els.payCash.value = paymentSplits.Cash > 0 ? moneyPlain(paymentSplits.Cash) : "";
    if (els.payWhish) els.payWhish.value = paymentSplits.Whish > 0 ? moneyPlain(paymentSplits.Whish) : "";
    if (els.payDebit) els.payDebit.value = paymentSplits.Debit > 0 ? moneyPlain(paymentSplits.Debit) : "";
    if (els.cashTenderedInput) {
      els.cashTenderedInput.value = cashTendered > 0 ? moneyPlain(cashTendered) : "";
    }
  }

  function paidTotal() {
    return PAYMENT_METHODS.reduce((sum, method) => sum + (paymentSplits[method] || 0), 0);
  }

  function paymentRemaining() {
    return Math.max(0, total() - paidTotal());
  }

  function effectiveCashTendered() {
    const cashPortion = paymentSplits.Cash || 0;
    if (cashPortion <= 0) return 0;
    return cashTendered > 0 ? cashTendered : cashPortion;
  }

  function cashChangeAmt() {
    const cashPortion = paymentSplits.Cash || 0;
    if (cashPortion <= 0 || cashTendered <= 0) return 0;
    return cashTendered - cashPortion;
  }

  function paymentMethodLabel() {
    const active = PAYMENT_METHODS.filter((m) => (paymentSplits[m] || 0) > 0.009);
    if (active.length === 0) return "Unpaid";
    if (active.length === 1) return active[0];
    return active.join(" + ");
  }

  function resetPaymentSplits() {
    paymentSplits = { Cash: 0, Whish: 0, Debit: 0 };
    cashTendered = 0;
    writePaymentSplitsToInputs();
    resetLbpHelper();
  }

  function payFullAmount(method) {
    const tot = total();
    if (tot <= 0) return;
    paymentSplits = { Cash: 0, Whish: 0, Debit: 0 };
    paymentSplits[method] = tot;
    cashTendered = 0;
    writePaymentSplitsToInputs();
    if (method === "Cash" && els.cashTenderedInput) {
      els.cashTenderedInput.value = "";
      els.cashTenderedInput.focus();
    }
    renderSummary();
  }

  function fillPaymentRemainder(method) {
    readPaymentSplitsFromInputs();
    const rem = paymentRemaining();
    if (rem <= 0.009) return;
    paymentSplits[method] = Math.round(((paymentSplits[method] || 0) + rem) * 100) / 100;
    if (method === "Cash" && cashTendered > 0 && cashTendered < paymentSplits.Cash) {
      cashTendered = paymentSplits.Cash;
    }
    writePaymentSplitsToInputs();
    renderSummary();
  }

  function renderLbpHelper(changeUsd) {
    if (!els.lbpWrap) return;

    const ch = changeUsd ?? cashChangeAmt();
    const show = (paymentSplits.Cash || 0) > 0.009 && ch > 0.009;
    els.lbpWrap.classList.toggle("d-none", !show);
    if (!show) return;

    const totalLbp = ch * lbpPerUsd;
    if (els.lbpTotalChange) els.lbpTotalChange.textContent = formatLbp(totalLbp);

    let usdPortion = parseFloat(els.lbpUsd?.value) || 0;
    if (usdPortion > ch) {
      usdPortion = ch;
      if (els.lbpUsd) els.lbpUsd.value = moneyPlain(usdPortion);
    }

    const remainLbp = Math.max(0, ch - usdPortion) * lbpPerUsd;
    if (els.lbpRemain) {
      els.lbpRemain.textContent = formatLbp(remainLbp);
      els.lbpRemain.classList.toggle("bk-pos-v2-lbp-remain--zero", remainLbp < 0.5);
    }
  }

  function resetLbpHelper() {
    if (els.lbpUsd) els.lbpUsd.value = "";
    if (els.lbpWrap) els.lbpWrap.classList.add("d-none");
  }

  function renderSummary() {
    readPaymentSplitsFromInputs();

    const sub = subtotal();
    const tax = taxAmt();
    const tot = total();
    const paid = paidTotal();
    const due = paymentRemaining();
    const ch = cashChangeAmt();

    els.subtotal.textContent = money.format(sub);
    els.discount.textContent = "−" + money.format(effectiveDiscount());
    els.tax.textContent = money.format(tax);
    els.total.textContent = money.format(tot);

    if (els.payPaid) els.payPaid.textContent = money.format(paid);
    if (els.payRemaining) els.payRemaining.textContent = money.format(due);
    if (els.payRemainingWrap) {
      els.payRemainingWrap.classList.toggle("bk-pos-v2-payment-remaining--ok", due <= 0.009);
      els.payRemainingWrap.classList.toggle("bk-pos-v2-payment-remaining--due", due > 0.009);
    }

    const hasCash = (paymentSplits.Cash || 0) > 0.009;
    if (els.cashTenderedWrap) els.cashTenderedWrap.classList.toggle("d-none", !hasCash);

    if (hasCash && cashTendered > 0) {
      els.changeWrap.classList.remove("d-none");
      const ok = ch >= -0.009;
      els.changeWrap.classList.toggle("bk-pos-v2-change--bad", !ok);
      els.changeLabel.textContent = ok ? "Change" : "Cash short";
      els.change.textContent = money.format(Math.abs(ch));
    } else {
      els.changeWrap.classList.add("d-none");
    }

    renderLbpHelper(ch);

    const debitOk = (paymentSplits.Debit || 0) <= 0.009 || !!selectedCustomer;
    const paymentOk =
      due <= 0.009 &&
      (paymentSplits.Cash <= 0.009 || cashTendered <= 0.009 || cashChangeAmt() >= -0.009) &&
      debitOk;
    const completeOk = cart.size > 0 && paymentOk;
    els.complete.disabled = !completeOk;
    els.print.disabled = cart.size === 0 && !lastReceiptSnapshot;
  }

  function applyStockFromSale(snapshot) {
    for (const ln of snapshot.lines) {
      const p = catalog.find((x) => x.id === ln.id);
      if (p) p.stock = Math.max(0, p.stock - ln.qty);
    }
  }

  function renderCustomerDd() {
    const q = (els.customerSearch.value || "").trim().toLowerCase();
    let list = customers;
    if (q) {
      list = customers.filter(
        (c) =>
          c.name.toLowerCase().includes(q) ||
          (c.email && c.email.toLowerCase().includes(q)) ||
          (c.phone && c.phone.toLowerCase().includes(q))
      );
    }
    list = list.slice(0, 5);
    if (!list.length) {
      els.customerDd.innerHTML = `<div class="bk-pos-v2-dd-empty">No customers found</div>`;
    } else {
      els.customerDd.innerHTML = list
        .map(
          (c) => `<button type="button" class="bk-pos-v2-dd-item" data-cust-id="${escapeAttr(c.id)}">
            <div class="bk-pos-v2-dd-item-title">${escapeHtml(c.name)}</div>
            <div class="bk-pos-v2-dd-item-sub">${escapeHtml(c.email || "—")}</div>
          </button>`
        )
        .join("");
    }
    els.customerDd.classList.remove("d-none");
  }

  function productSearchHaystack(p) {
    return [
      p.name || "",
      p.sku || "",
      p.barcode != null ? String(p.barcode) : "",
      p.category || "",
      String(p.id || ""),
    ]
      .join(" ")
      .toLowerCase();
  }

  /** All query words must appear somewhere in the product fields (any order). */
  function productMatchesTokens(p, tokens) {
    const hay = productSearchHaystack(p);
    return tokens.every((t) => hay.includes(t));
  }

  /** Lower score = better match (name hits preferred over sku/category). */
  function productMatchScore(p, tokens, raw) {
    const name = (p.name || "").toLowerCase();
    const sku = (p.sku || "").toLowerCase();
    const barcode = p.barcode != null ? String(p.barcode).toLowerCase() : "";
    if (name === raw) return 0;
    if (sku === raw || barcode === raw) return 1;
    if (name.startsWith(raw)) return 2;
    if (name.includes(raw)) return 3;
    const nameHits = tokens.filter((t) => name.includes(t)).length;
    if (nameHits === tokens.length) return 4;
    if (sku.startsWith(raw) || barcode.startsWith(raw)) return 5;
    return 6 + (tokens.length - nameHits);
  }

  function filterProducts() {
    const raw = (els.productSearch.value || "").trim().toLowerCase();
    if (!raw) return [];
    const tokens = raw.split(/\s+/).filter(Boolean);
    return catalog
      .filter((p) => productMatchesTokens(p, tokens))
      .sort((a, b) => {
        const sa = productMatchScore(a, tokens, raw);
        const sb = productMatchScore(b, tokens, raw);
        if (sa !== sb) return sa - sb;
        return (a.name || "").localeCompare(b.name || "");
      })
      .slice(0, 10);
  }

  function renderProductDd() {
    const raw = (els.productSearch.value || "").trim();
    if (!raw) {
      hideProductDd();
      els.productDd.innerHTML = "";
      return;
    }
    const list = filterProducts();
    if (!list.length) {
      els.productDd.innerHTML = `<div class="bk-pos-v2-dd-empty">No products match</div>`;
    } else {
      els.productDd.innerHTML = list
        .map((p) => {
          const stockOk = p.stock > 0;
          const badge = stockOk
            ? `<span class="bk-pos-v2-stock ok">Stock: ${p.stock}</span>`
            : `<span class="bk-pos-v2-stock bad">Out of stock</span>`;
          return `<button type="button" class="bk-pos-v2-dd-item bk-pos-v2-dd-item--row" data-prod-id="${escapeAttr(p.id)}" ${
            stockOk ? "" : "disabled"
          }>
            <div class="bk-pos-v2-dd-item-grow">
              <div class="bk-pos-v2-dd-item-title">${escapeHtml(p.name)}</div>
              <div class="bk-pos-v2-dd-item-sub">${escapeHtml(p.sku)} · ${money.format(p.price)}</div>
            </div>
            ${badge}
          </button>`;
        })
        .join("");
    }
    els.productDd.classList.remove("d-none");
  }

  function hideCustomerDd() {
    els.customerDd.classList.add("d-none");
  }

  function hideProductDd() {
    els.productDd.classList.add("d-none");
  }

  function selectCustomer(c) {
    selectedCustomer = c;
    els.customerSearch.value = c.name;
    els.customerChipName.textContent = c.name;
    els.customerChip.classList.remove("d-none");
    hideCustomerDd();
    const session = getActiveSession();
    if (session) {
      session.selectedCustomer = c;
      renderSessionTabs();
    }
    renderSummary();
  }

  function clearCustomer() {
    selectedCustomer = null;
    els.customerSearch.value = "";
    els.customerChip.classList.add("d-none");
    const session = getActiveSession();
    if (session) {
      session.selectedCustomer = null;
      renderSessionTabs();
    }
    renderSummary();
  }

  function addProductToCart(id) {
    const p = catalog.find((x) => x.id === id);
    if (!p) return;
    if (p.stock <= 0) {
      showToast("Product is out of stock", true);
      return;
    }
    const existing = cart.get(id);
    if (existing) {
      if (existing.qty >= p.stock) {
        showToast("Insufficient stock", true);
        return;
      }
      existing.qty += 1;
    } else {
      cart.set(id, { product: p, qty: 1 });
    }
    els.productSearch.value = "";
    hideProductDd();
    renderCart();
    showToast("Product added to cart");
    focusProductSearch();
  }

  function focusProductSearch() {
    if (!els.productSearch) return;
    els.productSearch.focus({ preventScroll: true });
  }

  function removeLine(id) {
    cart.delete(id);
    renderCart();
  }

  function setQty(id, delta) {
    const row = cart.get(id);
    if (!row) return;
    const max = row.product.stock;
    let next = row.qty + delta;
    if (delta > 0 && next > max) {
      showToast("Insufficient stock", true);
      return;
    }
    next = Math.max(1, next);
    if (next > max) return;
    row.qty = next;
    renderCart();
  }

  function renderCart() {
    if (!cart.size) {
      els.cartLines.innerHTML = `<div class="bk-pos-v2-empty">
        <i class="bi bi-cart3 bk-pos-v2-empty-icon" aria-hidden="true"></i>
        <p class="bk-pos-v2-empty-title">Cart is empty</p>
        <p class="bk-pos-v2-empty-sub">Scan or search products to add to cart</p>
      </div>`;
    } else {
      els.cartLines.innerHTML = [...cart.values()]
        .map(({ product: p, qty }) => {
          const lineTotal = p.price * qty;
          return `<div class="bk-pos-v2-line" data-line="${escapeAttr(p.id)}">
            <div class="bk-pos-v2-line-main">
              <h3 class="bk-pos-v2-line-name">${escapeHtml(p.name)}</h3>
              <p class="bk-pos-v2-line-sku">${escapeHtml(p.sku)}</p>
              <div class="bk-pos-v2-line-controls">
                <div class="bk-pos-v2-stepper">
                  <button type="button" class="bk-pos-v2-step" data-qty="${escapeAttr(p.id)}" data-delta="-1" aria-label="Decrease quantity"><i class="bi bi-dash-lg"></i></button>
                  <span class="bk-pos-v2-step-val">${qty}</span>
                  <button type="button" class="bk-pos-v2-step" data-qty="${escapeAttr(p.id)}" data-delta="1" aria-label="Increase quantity"><i class="bi bi-plus-lg"></i></button>
                </div>
                <span class="bk-pos-v2-line-each">× ${money.format(p.price)}</span>
              </div>
            </div>
            <div class="bk-pos-v2-line-aside">
              <p class="bk-pos-v2-line-total">${money.format(lineTotal)}</p>
              <button type="button" class="bk-pos-v2-remove" data-remove="${escapeAttr(p.id)}" aria-label="Remove line"><i class="bi bi-trash"></i></button>
            </div>
          </div>`;
        })
        .join("");
    }
    renderSummary();
    renderSessionTabs();
  }

  function clearCart() {
    cart.clear();
    discountAmount = 0;
    issueInvoice = false;
    if (els.issueInvoice) els.issueInvoice.checked = false;
    resetPaymentSplits();
    els.promo.value = "";
    renderCart();
    showToast("Cart cleared");
  }

  function applyPromo() {
    const raw = (els.promo.value || "").trim();
    if (!raw) {
      discountAmount = 0;
      renderSummary();
      return;
    }
    const amount = parseFloat(raw);
    if (!Number.isFinite(amount) || amount < 0) {
      discountAmount = 0;
      showToast("Enter a valid discount amount", true);
      renderSummary();
      return;
    }
    discountAmount = amount;
    const applied = effectiveDiscount();
    showToast(
      applied > 0
        ? `Discount applied: ${money.format(applied)}`
        : `${money.format(amount)} discount ready — add items to cart`
    );
    renderSummary();
  }

  function openModal(name) {
    if (name === "scan") els.modalScan.classList.remove("d-none");
    if (name === "customer") els.modalCustomer.classList.remove("d-none");
    if (name === "receipt" && els.modalReceipt) els.modalReceipt.classList.remove("d-none");
  }

  function closeModal(name) {
    if (name === "scan") els.modalScan.classList.add("d-none");
    if (name === "customer") {
      els.modalCustomer.classList.add("d-none");
      els.newCustName.value = "";
      els.newCustEmail.value = "";
      els.newCustPhone.value = "";
    }
    if (name === "receipt" && els.modalReceipt) els.modalReceipt.classList.add("d-none");
  }

  function formatSaleNumber(d) {
    const pad = (n, w) => String(n).padStart(w, "0");
    return (
      "SAL-" +
      pad(d.getFullYear(), 4) +
      pad(d.getMonth() + 1, 2) +
      pad(d.getDate(), 2) +
      "-" +
      pad(d.getHours(), 2) +
      pad(d.getMinutes(), 2) +
      pad(d.getSeconds(), 2)
    );
  }

  function buildSaleSnapshot() {
    const completedAt = new Date();
    return {
      saleNumber: formatSaleNumber(completedAt),
      completedAt: completedAt.toISOString(),
      storeName,
      customer: selectedCustomer
        ? {
            id: selectedCustomer.id,
            name: selectedCustomer.name,
            email: selectedCustomer.email,
            phone: selectedCustomer.phone,
          }
        : null,
      lines: [...cart.values()].map(({ product: p, qty }) => ({
        id: p.id,
        name: p.name,
        sku: p.sku,
        qty,
        unitPrice: p.price,
        lineTotal: p.price * qty,
      })),
      paymentMethod: paymentMethodLabel(),
      paymentSplits: PAYMENT_METHODS.filter((m) => (paymentSplits[m] || 0) > 0).map((m) => ({
        method: m,
        amount: paymentSplits[m],
      })),
      subtotal: subtotal(),
      discount: effectiveDiscount(),
      tax: taxAmt(),
      total: total(),
      taxPercent: taxPctLabel,
      cashTendered: effectiveCashTendered(),
      change: paymentSplits.Cash > 0 && cashTendered > 0 ? cashChangeAmt() : null,
      issueInvoice,
      promo: null,
    };
  }

  function formatInvoiceNumber(saleNumber) {
    return String(saleNumber || "").replace(/^SAL-/, "INV-") || "INV-DRAFT";
  }

  function formatDocDate(iso) {
    return new Date(iso).toLocaleDateString(undefined, {
      year: "numeric",
      month: "long",
      day: "numeric",
    });
  }

  function renderReceiptHtml(s) {
    const when = new Date(s.completedAt).toLocaleString();
    const lineRows = s.lines
      .map(
        (ln) => `<tr>
          <td class="bk-rcpt-item">
            <span class="bk-rcpt-item-name">${escapeHtml(ln.name)}</span>
            <span class="bk-rcpt-item-meta">${ln.qty} × ${money.format(ln.unitPrice)} · ${escapeHtml(ln.sku)}</span>
          </td>
          <td class="bk-rcpt-amt">${money.format(ln.lineTotal)}</td>
        </tr>`
      )
      .join("");

    const customerBlock = s.customer
      ? `<p class="bk-rcpt-customer"><strong>Customer</strong><br/>${escapeHtml(s.customer.name)}${
          s.customer.email ? `<br/>${escapeHtml(s.customer.email)}` : ""
        }${s.customer.phone ? `<br/>${escapeHtml(s.customer.phone)}` : ""}</p>`
      : "";

    const splitRows = (s.paymentSplits || [])
      .map(
        (p) =>
          `<div class="bk-rcpt-row"><span>${escapeHtml(p.method)}</span><span>${money.format(p.amount)}</span></div>`
      )
      .join("");
    const changeRow =
      s.change != null && s.change > 0.009
        ? `<div class="bk-rcpt-row bk-rcpt-row--emph"><span>Change</span><span>${money.format(s.change)}</span></div>`
        : "";
    const payExtra = splitRows
      ? `${splitRows}${changeRow}`
      : `<div class="bk-rcpt-row"><span>Payment</span><span>${escapeHtml(s.paymentMethod || "—")}</span></div>`;

    const promoLine = s.promo
      ? `<div class="bk-rcpt-row"><span>Promo</span><span>${escapeHtml(s.promo)}</span></div>`
      : "";

    const journalLine = s.journalReference
      ? `<p class="bk-rcpt-meta-line"><span>GL</span> <strong>${escapeHtml(s.journalReference)}</strong></p>`
      : "";

    return `<header class="bk-rcpt-head">
        <p class="bk-rcpt-store">${escapeHtml(s.storeName)}</p>
        <p class="bk-rcpt-tag">Sales receipt</p>
      </header>
      <p class="bk-rcpt-meta-line"><span>Sale #</span> <strong>${escapeHtml(s.saleNumber)}</strong></p>
      <p class="bk-rcpt-meta-line">${escapeHtml(when)}</p>
      ${journalLine}
      ${customerBlock}
      <table class="bk-rcpt-table" aria-label="Line items">
        <thead><tr><th>Item</th><th class="text-end">Amount</th></tr></thead>
        <tbody>${lineRows}</tbody>
      </table>
      <div class="bk-rcpt-totals">
        <div class="bk-rcpt-row"><span>Subtotal</span><span>${money.format(s.subtotal)}</span></div>
        <div class="bk-rcpt-row"><span>Discount</span><span>−${money.format(s.discount)}</span></div>
        ${promoLine}
        <div class="bk-rcpt-row"><span>Tax (${escapeHtml(s.taxPercent)}%)</span><span>${money.format(s.tax)}</span></div>
        <div class="bk-rcpt-row bk-rcpt-row--total"><span>Total</span><span>${money.format(s.total)}</span></div>
        ${payExtra}
      </div>
      <p class="bk-rcpt-thanks">Thank you for your purchase!</p>
      <p class="bk-rcpt-foot">${escapeHtml(receiptFooter)}</p>`;
  }

  function renderInvoiceHtml(s) {
    const invoiceNo = formatInvoiceNumber(s.saleNumber);
    const invoiceDate = formatDocDate(s.completedAt);
    const customer = s.customer;
    const billTo = customer
      ? `<strong>${escapeHtml(customer.name)}</strong>${
          customer.email ? `<br/>${escapeHtml(customer.email)}` : ""
        }${customer.phone ? `<br/>${escapeHtml(customer.phone)}` : ""}`
      : `<span class="bk-inv-muted">No customer on file</span>`;

    const lineRows = s.lines
      .map(
        (ln) => `<tr>
          <td>${escapeHtml(ln.name)}<span class="bk-inv-line-sku">${escapeHtml(ln.sku)}</span></td>
          <td class="bk-inv-num">${ln.qty}</td>
          <td class="bk-inv-num">${money.format(ln.unitPrice)}</td>
          <td class="bk-inv-num bk-inv-amt">${money.format(ln.lineTotal)}</td>
        </tr>`
      )
      .join("");

    const payRows = (s.paymentSplits || [])
      .map((p) => `<tr><td>${escapeHtml(p.method)}</td><td class="bk-inv-num">${money.format(p.amount)}</td></tr>`)
      .join("");

    const changeRow =
      s.change != null && s.change > 0.009
        ? `<tr><td>Change (cash)</td><td class="bk-inv-num">${money.format(s.change)}</td></tr>`
        : "";

    return `<article class="bk-inv">
      <header class="bk-inv-head">
        <div class="bk-inv-from">
          <p class="bk-inv-eyebrow">From</p>
          <h2 class="bk-inv-store">${escapeHtml(s.storeName)}</h2>
        </div>
        <div class="bk-inv-title-block">
          <h1 class="bk-inv-title">Invoice</h1>
          <p class="bk-inv-no">${escapeHtml(invoiceNo)}</p>
        </div>
      </header>
      <div class="bk-inv-meta">
        <div class="bk-inv-meta-col">
          <p class="bk-inv-eyebrow">Bill to</p>
          <div class="bk-inv-billto">${billTo}</div>
        </div>
        <div class="bk-inv-meta-col bk-inv-meta-col--right">
          <p><span class="bk-inv-eyebrow">Invoice date</span><br/><strong>${escapeHtml(invoiceDate)}</strong></p>
          <p><span class="bk-inv-eyebrow">Sale reference</span><br/><strong>${escapeHtml(s.saleNumber)}</strong></p>
        </div>
      </div>
      <table class="bk-inv-table" aria-label="Invoice line items">
        <thead>
          <tr>
            <th>Description</th>
            <th class="bk-inv-num">Qty</th>
            <th class="bk-inv-num">Unit</th>
            <th class="bk-inv-num">Amount</th>
          </tr>
        </thead>
        <tbody>${lineRows}</tbody>
      </table>
      <div class="bk-inv-bottom">
        <div class="bk-inv-pay">
          <p class="bk-inv-eyebrow">Payment</p>
          <table class="bk-inv-pay-table">
            <tbody>${payRows}${changeRow}</tbody>
          </table>
        </div>
        <div class="bk-inv-totals">
          <div class="bk-inv-total-row"><span>Subtotal</span><span>${money.format(s.subtotal)}</span></div>
          <div class="bk-inv-total-row"><span>Discount</span><span>−${money.format(s.discount)}</span></div>
          <div class="bk-inv-total-row"><span>Tax (${escapeHtml(s.taxPercent)}%)</span><span>${money.format(s.tax)}</span></div>
          <div class="bk-inv-total-row bk-inv-total-row--grand"><span>Total due</span><span>${money.format(s.total)}</span></div>
        </div>
      </div>
      <footer class="bk-inv-foot">${escapeHtml(receiptFooter)}</footer>
    </article>`;
  }

  function renderDocumentHtml(snapshot, view) {
    return view === "invoice" ? renderInvoiceHtml(snapshot) : renderReceiptHtml(snapshot);
  }

  function switchDocView(view) {
    activeDocView = view === "invoice" ? "invoice" : "receipt";
    if (els.saleDocPanel) {
      els.saleDocPanel.classList.toggle("bk-pos-v2-modal-panel--invoice", activeDocView === "invoice");
    }
    if (els.receiptPrintArea) {
      els.receiptPrintArea.classList.toggle("bk-pos-v2-receipt--invoice", activeDocView === "invoice");
    }
    root.querySelectorAll("[data-doc-view]").forEach((btn) => {
      const isActive = btn.getAttribute("data-doc-view") === activeDocView;
      btn.classList.toggle("is-active", isActive);
      btn.setAttribute("aria-selected", isActive ? "true" : "false");
    });
    if (pendingReceiptSnapshot && els.receiptBody) {
      els.receiptBody.innerHTML = renderDocumentHtml(pendingReceiptSnapshot, activeDocView);
    }
  }

  function showReceiptModal(snapshot) {
    lastReceiptSnapshot = snapshot;
    pendingReceiptSnapshot = snapshot;
    if (!els.receiptBody || !els.modalReceipt) {
      if (snapshot.issueInvoice) printInvoiceFromSnapshot(snapshot);
      else printReceiptFromSnapshot(snapshot);
      return;
    }
    activeDocView = snapshot.issueInvoice ? "invoice" : "receipt";
    switchDocView(activeDocView);
    openModal("receipt");
  }

  function openPrintWindow(title, innerHtml, css, width, height) {
    const html = `<!DOCTYPE html><html><head><meta charset="utf-8"/><title>${escapeHtml(title)}</title>
      <style>${css}</style></head><body>${innerHtml}
      <script>window.onload=function(){window.print();setTimeout(function(){window.close()},300)}<\/script>
      </body></html>`;
    const w = window.open("", "_blank", `width=${width},height=${height}`);
    if (!w) {
      showToast("Allow pop-ups to print", true);
      return;
    }
    w.document.write(html);
    w.document.close();
  }

  const receiptPrintCss = `
    *{box-sizing:border-box}
    body{font-family:"Courier New",Courier,monospace;font-size:13px;line-height:1.45;color:#111;max-width:320px;margin:0 auto;padding:16px}
    .bk-rcpt-head{text-align:center;margin-bottom:12px;border-bottom:1px dashed #999;padding-bottom:10px}
    .bk-rcpt-store{font-size:16px;font-weight:700;margin:0 0 4px}
    .bk-rcpt-tag{margin:0;font-size:11px;text-transform:uppercase;letter-spacing:.12em}
    .bk-rcpt-meta-line{margin:4px 0;font-size:12px}
    .bk-rcpt-customer{margin:10px 0;font-size:12px}
    .bk-rcpt-table{width:100%;border-collapse:collapse;margin:12px 0}
    .bk-rcpt-table th,.bk-rcpt-table td{padding:6px 0;vertical-align:top;border-bottom:1px dotted #ccc}
    .bk-rcpt-table th{font-size:11px;text-transform:uppercase}
    .bk-rcpt-item-name{display:block;font-weight:600}
    .bk-rcpt-item-meta{display:block;font-size:11px;color:#444;margin-top:2px}
    .bk-rcpt-amt{text-align:right;white-space:nowrap;font-weight:600}
    .bk-rcpt-totals{margin-top:8px;border-top:1px dashed #999;padding-top:8px}
    .bk-rcpt-row{display:flex;justify-content:space-between;gap:8px;margin:4px 0;font-size:12px}
    .bk-rcpt-row--total{font-size:15px;font-weight:700;margin-top:6px}
    .bk-rcpt-row--emph{font-weight:700}
    .bk-rcpt-thanks{text-align:center;margin:14px 0 4px;font-weight:700}
    .bk-rcpt-foot{text-align:center;font-size:11px;color:#555;margin:0}
    @media print{body{padding:0}}
  `;

  const invoicePrintCss = `
    *{box-sizing:border-box}
    body{font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;font-size:13px;line-height:1.5;color:#111;max-width:720px;margin:0 auto;padding:28px}
    .bk-inv-head{display:flex;justify-content:space-between;gap:24px;align-items:flex-start;margin-bottom:24px;padding-bottom:16px;border-bottom:2px solid #111}
    .bk-inv-eyebrow{margin:0 0 4px;font-size:10px;font-weight:700;text-transform:uppercase;letter-spacing:.1em;color:#666}
    .bk-inv-store{margin:0;font-size:18px;font-weight:800}
    .bk-inv-title-block{text-align:right}
    .bk-inv-title{margin:0;font-size:28px;font-weight:800;letter-spacing:.04em}
    .bk-inv-no{margin:4px 0 0;font-size:13px;font-weight:700;color:#444}
    .bk-inv-meta{display:flex;justify-content:space-between;gap:24px;margin-bottom:20px}
    .bk-inv-meta-col--right{text-align:right}
    .bk-inv-billto{line-height:1.45}
    .bk-inv-muted{color:#666}
    .bk-inv-table{width:100%;border-collapse:collapse;margin:16px 0}
    .bk-inv-table th,.bk-inv-table td{padding:10px 8px;border-bottom:1px solid #ddd;vertical-align:top}
    .bk-inv-table th{font-size:10px;text-transform:uppercase;letter-spacing:.08em;color:#666;text-align:left}
    .bk-inv-line-sku{display:block;font-size:11px;color:#666;margin-top:2px}
    .bk-inv-num{text-align:right;white-space:nowrap}
    .bk-inv-amt{font-weight:700}
    .bk-inv-bottom{display:flex;justify-content:space-between;gap:32px;align-items:flex-start;margin-top:8px}
    .bk-inv-pay{flex:1}
    .bk-inv-pay-table{width:100%;border-collapse:collapse}
    .bk-inv-pay-table td{padding:4px 0;font-size:12px}
    .bk-inv-totals{min-width:220px}
    .bk-inv-total-row{display:flex;justify-content:space-between;gap:12px;padding:4px 0;font-size:13px}
    .bk-inv-total-row--grand{margin-top:8px;padding-top:8px;border-top:2px solid #111;font-size:16px;font-weight:800}
    .bk-inv-foot{margin-top:28px;padding-top:12px;border-top:1px solid #ddd;font-size:11px;color:#666}
    @media print{body{padding:16px}}
  `;

  function printReceiptFromSnapshot(snapshot) {
    const s = snapshot || lastReceiptSnapshot;
    if (!s || !s.lines.length) {
      showToast("Nothing to print", true);
      return;
    }
    openPrintWindow(
      `Receipt ${s.saleNumber}`,
      renderReceiptHtml(s),
      receiptPrintCss,
      400,
      720
    );
  }

  function printInvoiceFromSnapshot(snapshot) {
    const s = snapshot || pendingReceiptSnapshot || lastReceiptSnapshot;
    if (!s || !s.lines.length) {
      showToast("Nothing to print", true);
      return;
    }
    if (!s.customer) {
      showToast("Select a customer to print an invoice", true);
      return;
    }
    openPrintWindow(
      `Invoice ${formatInvoiceNumber(s.saleNumber)}`,
      renderInvoiceHtml(s),
      invoicePrintCss,
      820,
      900
    );
  }

  let toastTimer = null;
  function showToast(message, isError) {
    els.toastMsg.textContent = message;
    els.toastIcon.className = "bi bk-pos-v2-toast-icon " + (isError ? "bi-exclamation-circle text-danger" : "bi-check-circle text-success");
    els.toast.classList.remove("d-none");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
      els.toast.classList.add("d-none");
    }, 3200);
  }

  function printReceipt() {
    if (cart.size) {
      printReceiptFromSnapshot(buildSaleSnapshot());
      return;
    }
    if (lastReceiptSnapshot) {
      printReceiptFromSnapshot(lastReceiptSnapshot);
      return;
    }
    showToast("Nothing to print", true);
  }

  function buildCompleteSalePayload(snapshot) {
    return {
      paymentMethod: snapshot.paymentMethod,
      paymentSplits: snapshot.paymentSplits || [],
      cashTendered: snapshot.cashTendered || 0,
      discount: snapshot.discount,
      amountReceived: snapshot.cashTendered || 0,
      promoCode: snapshot.promo || null,
      customer: snapshot.customer
        ? {
            id: snapshot.customer.id || null,
            name: snapshot.customer.name,
            email: snapshot.customer.email || null,
            phone: snapshot.customer.phone || null,
          }
        : null,
      lines: snapshot.lines.map((ln) => ({
        productId: ln.id || null,
        sku: ln.sku,
        productName: ln.name,
        quantity: ln.qty,
        unitPrice: ln.unitPrice,
      })),
    };
  }

  async function postCompleteSale(snapshot) {
    if (!completeSaleUrl || !antiforgeryToken) {
      return { ok: false, error: "Sale posting is not configured." };
    }

    const res = await fetch(completeSaleUrl, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        RequestVerificationToken: antiforgeryToken,
      },
      body: JSON.stringify(buildCompleteSalePayload(snapshot)),
    });

    let data = {};
    try {
      data = await res.json();
    } catch {
      /* ignore */
    }

    if (!res.ok) {
      return { ok: false, error: data.error || "Could not complete sale." };
    }

    return { ok: true, data };
  }

  async function completeSale() {
    if (!cart.size) {
      showToast("Cart is empty", true);
      return;
    }
    readPaymentSplitsFromInputs();
    if (paymentRemaining() > 0.009) {
      showToast("Payment does not cover the total", true);
      return;
    }
    if ((paymentSplits.Cash || 0) > 0 && cashTendered > 0 && cashChangeAmt() < -0.009) {
      showToast("Insufficient cash tendered", true);
      return;
    }

    issueInvoice = !!els.issueInvoice?.checked;
    if (issueInvoice && !selectedCustomer) {
      showToast("Select a customer to issue an invoice", true);
      return;
    }
    if ((paymentSplits.Debit || 0) > 0.009 && !selectedCustomer) {
      showToast("Add a customer before completing a debit sale", true);
      els.customerSearch?.focus();
      return;
    }

    const snapshot = buildSaleSnapshot();
    els.complete.disabled = true;

    try {
      const posted = await postCompleteSale(snapshot);
      if (!posted.ok) {
        showToast(posted.error, true);
        return;
      }

      snapshot.saleNumber = posted.data.saleNumber || snapshot.saleNumber;
      snapshot.journalReference = posted.data.journalReference || null;
      applyStockFromSale(snapshot);
      showReceiptModal(snapshot);
      removeSessionAfterSale();

      const journalHint = snapshot.journalReference
        ? ` · ${snapshot.journalReference}`
        : "";
      showToast(`Sale ${snapshot.saleNumber} completed${journalHint}`);
    } catch {
      showToast("Network error — sale was not saved.", true);
    } finally {
      els.complete.disabled = false;
      renderSummary();
    }
  }

  /* Events */
  els.customerSearch.addEventListener("focus", () => {
    renderCustomerDd();
  });
  els.customerSearch.addEventListener("input", () => {
    if (!selectedCustomer || els.customerSearch.value !== selectedCustomer.name) {
      selectedCustomer = null;
      els.customerChip.classList.add("d-none");
      renderSummary();
    }
    renderCustomerDd();
  });
  els.customerDd.addEventListener("mousedown", (e) => e.preventDefault());
  els.customerDd.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-cust-id]");
    if (!btn) return;
    const id = btn.getAttribute("data-cust-id");
    const c = customers.find((x) => x.id === id);
    if (c) selectCustomer(c);
  });

  els.customerClear.addEventListener("click", clearCustomer);

  els.customerAdd.addEventListener("click", () => openModal("customer"));

  els.productSearch.addEventListener("focus", () => {
    renderProductDd();
  });
  els.productSearch.addEventListener("input", () => {
    renderProductDd();
  });
  els.productSearch.addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      renderProductDd();
    }
  });
  els.productDd.addEventListener("mousedown", (e) => e.preventDefault());
  els.productDd.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-prod-id]");
    if (!btn || btn.disabled) return;
    addProductToCart(btn.getAttribute("data-prod-id"));
  });

  document.addEventListener("click", (e) => {
    if (!root.contains(e.target)) return;
    if (!els.customerSearch.contains(e.target) && !els.customerDd.contains(e.target)) hideCustomerDd();
    if (!els.productSearch.contains(e.target) && !els.productDd.contains(e.target)) hideProductDd();
  });

  els.cartLines.addEventListener("click", (e) => {
    const rm = e.target.closest("[data-remove]");
    if (rm) {
      removeLine(rm.getAttribute("data-remove"));
      return;
    }
    const q = e.target.closest("[data-qty]");
    if (!q) return;
    const id = q.getAttribute("data-qty");
    const delta = parseInt(q.getAttribute("data-delta") || "0", 10);
    setQty(id, delta);
  });

  els.cartClear.addEventListener("click", clearCart);

  root.querySelectorAll(".bk-pos-v2-payment-input").forEach((input) => {
    input.addEventListener("input", () => renderSummary());
  });

  if (els.cashTenderedInput) {
    els.cashTenderedInput.addEventListener("input", () => renderSummary());
  }

  root.querySelectorAll("[data-pay-fill]").forEach((btn) => {
    btn.addEventListener("click", () => fillPaymentRemainder(btn.getAttribute("data-pay-fill") || "Cash"));
  });

  root.querySelectorAll("[data-pay-all]").forEach((btn) => {
    btn.addEventListener("click", () => payFullAmount(btn.getAttribute("data-pay-all") || "Cash"));
  });

  if (els.lbpUsd) {
    els.lbpUsd.addEventListener("input", () => renderSummary());
  }

  els.promoApply.addEventListener("click", applyPromo);
  els.promo.addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      applyPromo();
    }
  });

  els.complete.addEventListener("click", completeSale);
  els.print.addEventListener("click", printReceipt);
  if (els.receiptPrint) {
    els.receiptPrint.addEventListener("click", () => printReceiptFromSnapshot(pendingReceiptSnapshot));
  }

  if (els.invoicePrint) {
    els.invoicePrint.addEventListener("click", () => printInvoiceFromSnapshot(pendingReceiptSnapshot));
  }

  if (els.issueInvoice) {
    els.issueInvoice.addEventListener("change", () => {
      issueInvoice = els.issueInvoice.checked;
    });
  }

  root.querySelectorAll("[data-doc-view]").forEach((btn) => {
    btn.addEventListener("click", () => switchDocView(btn.getAttribute("data-doc-view") || "receipt"));
  });

  if (els.scanOpen) {
    els.scanOpen.addEventListener("click", () => openModal("scan"));
  }

  if (els.newSession) {
    els.newSession.addEventListener("click", createNewSession);
  }

  if (els.sessionsBar) {
    els.sessionsBar.addEventListener("click", (e) => {
      const closeBtn = e.target.closest("[data-session-close]");
      if (closeBtn) {
        e.stopPropagation();
        closeSession(closeBtn.getAttribute("data-session-close"));
        return;
      }
      const tab = e.target.closest("[data-session-id]");
      if (!tab) return;
      switchSession(tab.getAttribute("data-session-id"));
    });
  }

  root.addEventListener("click", (e) => {
    const t = e.target.closest("[data-close-modal]");
    if (t) closeModal(t.getAttribute("data-close-modal"));
  });

  els.newCustSave.addEventListener("click", () => {
    const name = (els.newCustName.value || "").trim();
    if (!name) {
      showToast("Customer name is required", true);
      return;
    }
    const id = "c" + Date.now();
    const c = {
      id,
      name,
      email: (els.newCustEmail.value || "").trim(),
      phone: (els.newCustPhone.value || "").trim(),
    };
    customers.push(c);
    selectCustomer(c);
    closeModal("customer");
    showToast("Customer added");
  });

  els.toastClose.addEventListener("click", () => {
    els.toast.classList.add("d-none");
    clearTimeout(toastTimer);
  });

  document.addEventListener("keydown", (e) => {
    if (e.key !== "/") return;
    const el = document.activeElement;
    const tag = el && el.tagName;
    if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return;
    e.preventDefault();
    els.productSearch.focus();
  });

  initSessions();
  renderCart();
  focusProductSearch();
  // Win over any later focus moves (session tabs, layout) so barcode readers work immediately.
  requestAnimationFrame(focusProductSearch);
  setTimeout(focusProductSearch, 0);
})();
