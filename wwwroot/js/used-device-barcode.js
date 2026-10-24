/**
 * Used device barcode — preview (JsBarcode) + print sticker with battery, storage, optional price.
 */
(function () {
  'use strict';

  function escapeHtml(s) {
    return String(s || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  function renderSvg(svg, value) {
    if (!svg || typeof JsBarcode !== 'function') return false;
    var code = (value || '').trim();
    if (!code) {
      svg.replaceChildren();
      return false;
    }
    try {
      JsBarcode(svg, code, {
        format: 'CODE128',
        displayValue: false,
        margin: 4,
        height: 48,
        width: 1.6,
        background: '#ffffff',
        lineColor: '#111111'
      });
      return true;
    } catch (err) {
      svg.replaceChildren();
      return false;
    }
  }

  function formatMoney(raw) {
    var n = Number(String(raw || '').replace(/[^0-9.-]/g, ''));
    if (!isFinite(n)) return '';
    try {
      return n.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
    } catch (err) {
      return '$' + n.toFixed(2);
    }
  }

  function includePriceOnLabel() {
    var el = document.getElementById('bk-used-barcode-include-price');
    return !!(el && el.checked);
  }

  function buildSpecs(battery, storage) {
    var parts = [];
    var bat = (battery || '').toString().trim();
    var stor = (storage || '').toString().trim();
    if (bat) {
      if (bat.indexOf('%') === -1 && /^\d+(\.\d+)?$/.test(bat)) bat = bat + '%';
      parts.push('Batt ' + bat);
    }
    if (stor) parts.push(stor);
    return parts.join(' · ');
  }

  function buildLabelHtml(opts) {
    var code = (opts.code || '').trim();
    var store = (opts.storeName || '').trim();
    var name = (opts.name || '').trim();
    var specs = buildSpecs(opts.battery, opts.storage);
    var price = opts.includePrice ? (opts.price || '').trim() : '';
    var lines = [];
    if (name) lines.push('<div class="line">' + escapeHtml(name) + '</div>');
    if (specs) lines.push('<div class="line">' + escapeHtml(specs) + '</div>');
    if (price) lines.push('<div class="line price">' + escapeHtml(price) + '</div>');

    return (
      '<!doctype html><html><head><meta charset="utf-8"><title>Used device barcode</title><style>' +
      'html,body{margin:0;padding:0;background:#fff;color:#111;' +
      'font-family:ui-sans-serif,system-ui,-apple-system,sans-serif}' +
      '.label{width:48mm;max-width:100%;padding:3mm 3mm 2.5mm;box-sizing:border-box}' +
      '.store{text-align:center;font-size:10px;font-weight:800;line-height:1.2;' +
      'margin:0 0 1.4mm;letter-spacing:.02em;word-break:break-word}' +
      '.bars{display:block;width:100%;text-align:center}' +
      '.bars svg{display:block;margin:0 auto;max-width:100%;height:auto}' +
      '.code{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-weight:700;' +
      'font-size:11px;margin:1.5mm 0 1mm;text-align:center;letter-spacing:.02em}' +
      '.meta{text-align:center}' +
      '.line{font-size:9px;font-weight:600;line-height:1.25;margin:0 0 0.6mm;' +
      'word-break:break-word}' +
      '.line.price{font-size:11px;font-weight:800;margin-top:0.8mm}' +
      '@page{margin:0;size:auto}' +
      '@media print{html,body{margin:0}.label{padding:2mm}}' +
      '</style></head><body><div class="label">' +
      (store ? '<div class="store">' + escapeHtml(store) + '</div>' : '') +
      '<div class="bars"><svg id="bc"></svg></div>' +
      '<div class="code">' +
      escapeHtml(code) +
      '</div>' +
      (lines.length ? '<div class="meta">' + lines.join('') + '</div>' : '') +
      '</div></body></html>'
    );
  }

  function printLabel(opts) {
    var code = (opts.code || '').trim();
    if (!code) return;

    var iframe = document.createElement('iframe');
    iframe.setAttribute('title', 'Print barcode label');
    iframe.setAttribute('aria-hidden', 'true');
    iframe.style.cssText =
      'position:fixed;right:0;bottom:0;width:0;height:0;border:0;opacity:0;pointer-events:none';
    document.body.appendChild(iframe);

    var doc = iframe.contentDocument || (iframe.contentWindow && iframe.contentWindow.document);
    if (!doc) {
      iframe.remove();
      window.alert('Could not prepare the barcode label for printing.');
      return;
    }

    doc.open();
    doc.write(buildLabelHtml(opts));
    doc.close();

    function cleanup() {
      setTimeout(function () {
        if (iframe.parentNode) iframe.parentNode.removeChild(iframe);
      }, 800);
    }

    function doPrint() {
      try {
        if (typeof JsBarcode === 'function') {
          var svg = doc.getElementById('bc');
          if (svg) {
            JsBarcode(svg, code, {
              format: 'CODE128',
              displayValue: false,
              margin: 0,
              height: 42,
              width: 1.4,
              background: '#ffffff',
              lineColor: '#111111'
            });
          }
        }
      } catch (err) {
        /* still try to print the text label */
      }

      var win = iframe.contentWindow;
      if (!win) {
        cleanup();
        return;
      }

      var printed = false;
      function runPrint() {
        if (printed) return;
        printed = true;
        try {
          win.focus();
          win.print();
        } catch (err) {
          window.alert('Could not open the print dialog.');
        } finally {
          cleanup();
        }
      }

      setTimeout(runPrint, 60);
    }

    if (doc.readyState === 'complete') doPrint();
    else iframe.onload = doPrint;
  }

  function readFormLabelFields() {
    var nameEl = document.getElementById('bk-used-name');
    var batteryEl = document.getElementById('bk-used-battery');
    var storageEl = document.getElementById('bk-used-storage');
    var priceEl = document.getElementById('bk-used-price');
    return {
      name: nameEl ? nameEl.value : '',
      battery: batteryEl ? batteryEl.value : '',
      storage: storageEl ? storageEl.value : '',
      price: formatMoney(priceEl ? priceEl.value : ''),
      includePrice: includePriceOnLabel()
    };
  }

  function bindFormPreview() {
    var input = document.getElementById('bk-used-barcode');
    var svg = document.getElementById('bk-used-barcode-svg');
    var textEl = document.getElementById('bk-used-barcode-text');
    var nameLabel = document.getElementById('bk-used-barcode-name-label');
    var specsLabel = document.getElementById('bk-used-barcode-specs-label');
    var priceLabel = document.getElementById('bk-used-barcode-price-label');
    var includePriceEl = document.getElementById('bk-used-barcode-include-price');
    if (!input || !svg) return function () {};

    function refresh() {
      var value = (input.value || '').trim().toUpperCase();
      if (input.value !== value) input.value = value;
      if (textEl) textEl.textContent = value || '—';
      renderSvg(svg, value);

      var fields = readFormLabelFields();
      if (nameLabel) {
        nameLabel.textContent = fields.name || '';
        nameLabel.classList.toggle('d-none', !fields.name);
      }
      var specs = buildSpecs(fields.battery, fields.storage);
      if (specsLabel) {
        specsLabel.textContent = specs;
        specsLabel.classList.toggle('d-none', !specs);
      }
      if (priceLabel) {
        var showPrice = fields.includePrice && !!fields.price;
        priceLabel.textContent = showPrice ? fields.price : '';
        priceLabel.classList.toggle('d-none', !showPrice);
      }
    }

    ['bk-used-barcode', 'bk-used-name', 'bk-used-battery', 'bk-used-storage', 'bk-used-price'].forEach(function (id) {
      var el = document.getElementById(id);
      if (el) el.addEventListener('input', refresh);
    });
    if (includePriceEl) includePriceEl.addEventListener('change', refresh);
    refresh();
    return refresh;
  }

  function initForm(options) {
    var input = document.getElementById('bk-used-barcode');
    var regenBtn = document.getElementById('bk-used-barcode-regen');
    var printBtn = document.getElementById('bk-used-barcode-print');
    var storeName = (options && options.storeName) || '';
    if (!input) return;

    bindFormPreview();

    if (regenBtn && options && options.nextBarcodeUrl) {
      regenBtn.addEventListener('click', function () {
        regenBtn.disabled = true;
        fetch(options.nextBarcodeUrl, { headers: { Accept: 'application/json' } })
          .then(function (r) {
            if (!r.ok) throw new Error('barcode');
            return r.json();
          })
          .then(function (data) {
            if (data && data.barcode) {
              input.value = String(data.barcode).toUpperCase();
              input.dispatchEvent(new Event('input', { bubbles: true }));
            }
          })
          .catch(function () {
            window.alert('Could not generate a new barcode. Try again.');
          })
          .finally(function () {
            regenBtn.disabled = false;
          });
      });
    }

    if (printBtn) {
      printBtn.addEventListener('click', function () {
        var fields = readFormLabelFields();
        printLabel({
          code: input.value,
          storeName: storeName,
          name: fields.name,
          battery: fields.battery,
          storage: fields.storage,
          price: fields.price,
          includePrice: fields.includePrice
        });
      });
    }
  }

  function bindDetailPricePreview(priceText) {
    var includePriceEl = document.getElementById('bk-used-barcode-include-price');
    var priceLabel = document.getElementById('bk-used-barcode-price-label');
    if (!includePriceEl || !priceLabel) return;

    function refresh() {
      var show = includePriceEl.checked && !!(priceText || '').trim();
      priceLabel.classList.toggle('d-none', !show);
    }

    includePriceEl.addEventListener('change', refresh);
    refresh();
  }

  function initDetail(options) {
    var svg = document.getElementById('bk-used-barcode-svg');
    var printBtn = document.getElementById('bk-used-barcode-print');
    var code = (options && options.code) || '';
    var price = (options && options.price) || '';
    var storeName = (options && options.storeName) || '';
    if (svg) renderSvg(svg, code);
    bindDetailPricePreview(price);

    function doPrint() {
      printLabel({
        code: code,
        storeName: storeName,
        name: (options && options.name) || '',
        battery: (options && options.battery) || '',
        storage: (options && options.storage) || '',
        price: price,
        includePrice: includePriceOnLabel()
      });
    }

    if (printBtn) printBtn.addEventListener('click', doPrint);
    if (options && options.autoPrint) setTimeout(doPrint, 250);
  }

  window.BiktalUsedDeviceBarcode = {
    initForm: initForm,
    initDetail: initDetail,
    printLabel: printLabel
  };
})();
