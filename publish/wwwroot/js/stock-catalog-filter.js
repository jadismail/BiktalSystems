(function () {
  'use strict';

  function plural(n) {
    return n === 1 ? 'SKU' : 'SKUs';
  }

  function tokensMatch(haystack, tokens) {
    for (var i = 0; i !== tokens.length; i += 1) {
      if (haystack.indexOf(tokens[i]) === -1) return false;
    }
    return true;
  }

  function applyFilter(input, clearBtn, meta, emptyRow, rows, totalN) {
    var raw = (input.value || '').trim().toLowerCase();
    var tokens = raw ? raw.split(/\s+/).filter(Boolean) : [];
    var visible = 0;
    for (var r = 0; r !== rows.length; r += 1) {
      var row = rows[r];
      var hay = (row.getAttribute('data-bk-search') || '').toLowerCase();
      var show = !tokens.length || tokensMatch(hay, tokens);
      row.classList.toggle('d-none', !show);
      if (show) visible += 1;
    }
    if (emptyRow) {
      emptyRow.classList.toggle('d-none', !(tokens.length && visible === 0));
    }
    if (meta) {
      if (!tokens.length) {
        meta.innerHTML = '<strong>' + totalN + '</strong> ' + plural(totalN);
      } else {
        meta.innerHTML =
          'Showing <strong>' +
          visible +
          '</strong> of <strong>' +
          totalN +
          '</strong> ' +
          plural(totalN);
      }
    }
    if (clearBtn) {
      clearBtn.classList.toggle('d-none', !raw.length);
    }
  }

  function init() {
    var input = document.getElementById('bk-stock-search-q');
    var clearBtn = document.getElementById('bk-stock-search-clear');
    var meta = document.getElementById('bk-stock-count-meta');
    var emptyRow = document.getElementById('bk-stock-filter-empty');
    var rows = document.querySelectorAll('tbody tr.bk-stock-row');
    if (!input || !rows.length) return;

    var totalRows = rows.length;
    var dataTotal = meta && meta.getAttribute('data-total');
    var parsed = dataTotal ? parseInt(dataTotal, 10) : totalRows;
    var totalN = isNaN(parsed) ? totalRows : Math.max(parsed, totalRows);

    function apply() {
      applyFilter(input, clearBtn, meta, emptyRow, rows, totalN);
    }

    input.addEventListener('input', apply);
    input.addEventListener('search', apply);
    if (clearBtn) {
      clearBtn.addEventListener('click', function () {
        input.value = '';
        input.focus();
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
