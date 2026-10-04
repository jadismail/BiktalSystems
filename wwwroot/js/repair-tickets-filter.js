/**
 * Repair tickets board filter — barcode, ticket #, device, customer.
 * Barcode scanners type into the search box and usually send Enter.
 */
(function () {
  'use strict';

  function tokensMatch(haystack, tokens) {
    for (var i = 0; i !== tokens.length; i += 1) {
      if (haystack.indexOf(tokens[i]) === -1) return false;
    }
    return true;
  }

  function init() {
    var input = document.getElementById('bk-repair-tickets-search-q');
    var clearBtn = document.getElementById('bk-repair-tickets-search-clear');
    var meta = document.getElementById('bk-repair-tickets-count-meta');
    var empty = document.getElementById('bk-repair-tickets-filter-empty');
    var cards = document.querySelectorAll('[data-bk-repair-card]');
    if (!input || !cards.length) return;

    var total = cards.length;

    function apply() {
      var raw = (input.value || '').trim().toLowerCase();
      var tokens = raw ? raw.split(/\s+/).filter(Boolean) : [];
      var visible = 0;

      for (var i = 0; i !== cards.length; i += 1) {
        var card = cards[i];
        var hay = (card.getAttribute('data-bk-search') || '').toLowerCase();
        var show = !tokens.length || tokensMatch(hay, tokens);
        card.classList.toggle('d-none', !show);
        if (show) visible += 1;
      }

      document.querySelectorAll('[data-bk-repair-col]').forEach(function (col) {
        var any = col.querySelector('[data-bk-repair-card]:not(.d-none)');
        var placeholder = col.querySelector('[data-bk-repair-col-empty]');
        if (placeholder) placeholder.classList.toggle('d-none', !!any);
      });

      if (empty) empty.classList.toggle('d-none', !(tokens.length && visible === 0));
      if (meta) {
        if (!tokens.length) {
          meta.innerHTML = '<strong>' + total + '</strong> ticket' + (total === 1 ? '' : 's');
        } else {
          meta.innerHTML =
            'Showing <strong>' +
            visible +
            '</strong> of <strong>' +
            total +
            '</strong> ticket' +
            (total === 1 ? '' : 's');
        }
      }
      if (clearBtn) clearBtn.classList.toggle('d-none', !raw.length);
    }

    input.addEventListener('input', apply);
    input.addEventListener('search', apply);
    input.addEventListener('keydown', function (e) {
      if (e.key !== 'Enter') return;
      e.preventDefault();
      apply();
      var matches = Array.prototype.slice.call(
        document.querySelectorAll('[data-bk-repair-card]:not(.d-none)')
      );
      if (!matches.length) return;
      var link =
        matches[0].tagName === 'A'
          ? matches[0]
          : matches[0].querySelector('a[href]');
      if (!link) return;
      if (matches.length === 1) {
        window.location.href = link.href;
        return;
      }
      link.focus();
    });

    if (clearBtn) {
      clearBtn.addEventListener('click', function () {
        input.value = '';
        input.focus();
        apply();
      });
    }

    apply();
    input.focus();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
