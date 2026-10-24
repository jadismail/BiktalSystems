(function () {
  'use strict';

  function tokensMatch(haystack, tokens) {
    for (var i = 0; i !== tokens.length; i += 1) {
      if (haystack.indexOf(tokens[i]) === -1) return false;
    }
    return true;
  }

  function applyFilter(input, clearBtn, soldBtn, meta, emptyRow, rows) {
    var raw = (input.value || '').trim().toLowerCase();
    var tokens = raw ? raw.split(/\s+/).filter(Boolean) : [];
    var showSold = !!(soldBtn && soldBtn.getAttribute('aria-pressed') === 'true');

    var visible = 0;
    for (var r = 0; r !== rows.length; r += 1) {
      var row = rows[r];
      var isSold = row.getAttribute('data-bk-sold') === '1';
      var hay = (row.getAttribute('data-bk-search') || '').toLowerCase();
      var textOk = !tokens.length || tokensMatch(hay, tokens);
      var show = showSold ? isSold && textOk : !isSold && textOk;

      row.classList.toggle('d-none', !show);
      if (show) visible += 1;
    }

    if (emptyRow) {
      emptyRow.classList.toggle('d-none', visible !== 0);
    }

    if (meta) {
      var activeN = parseInt(meta.getAttribute('data-active') || '0', 10) || 0;
      var soldN = parseInt(meta.getAttribute('data-sold') || '0', 10) || 0;
      if (showSold) {
        meta.innerHTML = tokens.length
          ? 'Showing <strong>' + visible + '</strong> of <strong>' + soldN + '</strong> sold'
          : '<strong>' + soldN + '</strong> sold';
      } else if (!tokens.length) {
        meta.innerHTML = '<strong>' + activeN + '</strong> active';
      } else {
        meta.innerHTML =
          'Showing <strong>' + visible + '</strong> of <strong>' + activeN + '</strong> active';
      }
    }

    if (clearBtn) {
      clearBtn.classList.toggle('d-none', !raw.length);
    }

    if (soldBtn) {
      soldBtn.classList.toggle('is-active', showSold);
      soldBtn.setAttribute('title', showSold ? 'Show active devices' : 'Show sold devices');
    }
  }

  function init() {
    var input = document.getElementById('bk-used-devices-search-q');
    var clearBtn = document.getElementById('bk-used-devices-search-clear');
    var soldBtn = document.getElementById('bk-used-devices-sold-toggle');
    var meta = document.getElementById('bk-used-devices-count-meta');
    var emptyRow = document.getElementById('bk-used-devices-filter-empty');
    var rows = document.querySelectorAll('tbody tr.bk-used-device-row');
    if (!rows.length) return;

    function apply() {
      applyFilter(input, clearBtn, soldBtn, meta, emptyRow, rows);
    }

    if (input) {
      input.addEventListener('input', apply);
      input.addEventListener('search', apply);
    }
    if (clearBtn && input) {
      clearBtn.addEventListener('click', function () {
        input.value = '';
        input.focus();
        apply();
      });
    }
    if (soldBtn) {
      soldBtn.addEventListener('click', function () {
        var on = soldBtn.getAttribute('aria-pressed') === 'true';
        soldBtn.setAttribute('aria-pressed', on ? 'false' : 'true');
        apply();
      });
    }
    apply();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
