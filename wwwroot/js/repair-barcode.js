/**
 * Repair ticket device barcode — preview (JsBarcode) + print sticker (no pop-up).
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

  function buildLabelHtml(opts) {
    var code = (opts.code || '').trim();
    var device = (opts.device || '').trim();
    var customer = (opts.customer || '').trim();
    var lines = [];
    if (device) lines.push('<div class="line">' + escapeHtml(device) + '</div>');
    if (customer) lines.push('<div class="line">' + escapeHtml(customer) + '</div>');

    return (
      '<!doctype html><html><head><meta charset="utf-8"><title>Repair barcode</title><style>' +
      'html,body{margin:0;padding:0;background:#fff;color:#111;' +
      'font-family:ui-sans-serif,system-ui,-apple-system,sans-serif}' +
      '.label{width:48mm;max-width:100%;padding:3mm 3mm 2.5mm;box-sizing:border-box}' +
      '.title{font-size:8px;font-weight:800;letter-spacing:.08em;text-transform:uppercase;' +
      'color:#555;margin:0 0 2mm}' +
      '.bars{display:block;width:100%;text-align:center}' +
      '.bars svg{display:block;margin:0 auto;max-width:100%;height:auto}' +
      '.code{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-weight:700;' +
      'font-size:11px;margin:1.5mm 0 1mm;text-align:center;letter-spacing:.02em}' +
      '.meta{text-align:center}' +
      '.line{font-size:9px;font-weight:600;line-height:1.25;margin:0 0 0.6mm;' +
      'word-break:break-word}' +
      '.line.muted{color:#555;font-weight:500}' +
      '@page{margin:0;size:auto}' +
      '@media print{html,body{margin:0}.label{padding:2mm}}' +
      '</style></head><body><div class="label">' +
      '<p class="title">Khulasa repair</p>' +
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

      // Give the browser a tick to paint the barcode SVG.
      setTimeout(runPrint, 60);
    }

    if (doc.readyState === 'complete') doPrint();
    else iframe.onload = doPrint;
  }

  function bindPreview(input, svg, textEl, deviceInput, deviceLabelEl, customerInput, customerLabelEl) {
    function refresh() {
      var value = (input.value || '').trim().toUpperCase();
      if (input.value !== value) input.value = value;
      if (textEl) textEl.textContent = value || '—';
      renderSvg(svg, value);
      if (deviceLabelEl) {
        var device = (deviceInput && deviceInput.value ? deviceInput.value : '').trim();
        deviceLabelEl.textContent = device || '';
        deviceLabelEl.classList.toggle('d-none', !device);
      }
      if (customerLabelEl) {
        var customer = (customerInput && customerInput.value ? customerInput.value : '').trim();
        customerLabelEl.textContent = customer || '';
        customerLabelEl.classList.toggle('d-none', !customer);
      }
    }
    input.addEventListener('input', refresh);
    if (deviceInput) deviceInput.addEventListener('input', refresh);
    if (customerInput) customerInput.addEventListener('input', refresh);
    refresh();
    return refresh;
  }

  function initCreate(options) {
    var input = document.getElementById('bk-repair-barcode');
    var svg = document.getElementById('bk-repair-barcode-svg');
    var textEl = document.getElementById('bk-repair-barcode-text');
    var deviceInput = document.getElementById('bk-repair-device');
    var deviceLabelEl = document.getElementById('bk-repair-barcode-device-label');
    var customerInput = document.getElementById('CustomerName');
    var customerLabelEl = document.getElementById('bk-repair-barcode-customer-label');
    var regenBtn = document.getElementById('bk-repair-barcode-regen');
    var printBtn = document.getElementById('bk-repair-barcode-print');
    if (!input || !svg) return;

    bindPreview(input, svg, textEl, deviceInput, deviceLabelEl, customerInput, customerLabelEl);

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
        printLabel({
          code: input.value,
          device: deviceInput ? deviceInput.value : '',
          customer: customerInput ? customerInput.value : ''
        });
      });
    }
  }

  function initDetail(options) {
    var svg = document.getElementById('bk-repair-barcode-svg');
    var printBtn = document.getElementById('bk-repair-barcode-print');
    var code = (options && options.code) || '';
    if (svg) renderSvg(svg, code);
    if (printBtn) {
      printBtn.addEventListener('click', function () {
        printLabel({
          code: code,
          device: (options && options.device) || '',
          customer: (options && options.customer) || ''
        });
      });
    }
    if (options && options.autoPrint) {
      setTimeout(function () {
        printLabel({
          code: code,
          device: (options && options.device) || '',
          customer: (options && options.customer) || ''
        });
      }, 250);
    }
  }

  window.BiktalRepairBarcode = {
    initCreate: initCreate,
    initDetail: initDetail,
    printLabel: printLabel
  };
})();
