function initializeShareButtons() {
    document.querySelectorAll('.share-button').forEach(function (button) {
        button.onclick = function (e) {
            e.preventDefault();
            e.stopPropagation();
            var dropdown = this.parentElement;
            var menu = dropdown.querySelector('.dropdown-menu');
            var isOpen = dropdown.classList.contains('show');
            if (!isOpen) {
                dropdown.classList.add('show');
                if (menu) menu.classList.add('show');
                this.setAttribute('aria-expanded', 'true');
            }
        };
    });

    document.onclick = function (e) {
        if (!e.target.closest('.share-dropdown')) {
            document.querySelectorAll('.share-dropdown.show').forEach(function (dropdown) {
                dropdown.classList.remove('show');
                var menu = dropdown.querySelector('.dropdown-menu');
                if (menu) menu.classList.remove('show');
                var button = dropdown.querySelector('.share-button');
                if (button) button.setAttribute('aria-expanded', 'false');
            });
        }
    };

    document.querySelectorAll('.copy-link-btn').forEach(function (button) {
        button.onclick = function (e) {
            e.stopPropagation();
            var url = this.getAttribute('data-url');
            var absoluteUrl = new URL(url, window.location.origin).href;
            navigator.clipboard.writeText(absoluteUrl).then(function () {
                showCopyFeedback(button, 'Link copied!', false);
            }).catch(function () { showCopyFeedback(button, 'Failed to copy', true); });
        };
    });

    document.querySelectorAll('.copy-embed-btn').forEach(function (button) {
        button.onclick = function (e) {
            e.stopPropagation();
            try {
                var url = this.getAttribute('data-url') || '';
                var titleAttr = this.getAttribute('data-title') || '';
                var itemsAttr = this.getAttribute('data-items'); // may be null

                var title;
                try { title = JSON.parse(titleAttr); } catch { title = titleAttr; }
                var embedUrl = url.endsWith('/embed') ? url : (url + '/embed');
                var absoluteUrl = new URL(embedUrl, window.location.origin).href;

                var width = 515;
                var height = 225; // default (card)

                // Playlist detection: rely on presence of data-items FIRST; fallback to URL pattern
                var hasItems = itemsAttr !== null && itemsAttr !== undefined && itemsAttr !== '';
                var isPlaylist = hasItems || /\/playlist\//i.test(embedUrl);

                var itemCount = NaN;
                if (hasItems) {
                    itemCount = parseInt(itemsAttr, 10);
                }

                if (isPlaylist) {
                    if (isNaN(itemCount)) {
                        // Attempt to infer from header text "X items"
                        var headerEl = button.closest('.card,body').querySelector('.card-body p, .embed-header p');
                        if (headerEl) {
                            var m = /^(\d+)\s+items?/i.exec(headerEl.textContent.trim());
                            if (m) itemCount = parseInt(m[1], 10);
                        }
                    }
                    if (isNaN(itemCount)) itemCount = 1; // conservative fallback
                    var BASE = 104; // 56 + 48
                    var ROW = 74;
                    var rows = Math.min(Math.max(itemCount, 0), 5);
                    height = BASE + (rows * ROW);
                    if (rows === 0) height = BASE; // empty playlist
                    if (height > 474) height = 474;
                }

                var iframeCode = '<iframe src="' + absoluteUrl + '" width="' + width + '" height="' + height + '" frameborder="0" allowtransparency="true" style="max-width:100%;border:0;" title="' + (title || '') + '"></iframe>';
                navigator.clipboard.writeText(iframeCode).then(function () {
                    showCopyFeedback(button, 'Embed code copied!', false);
                }).catch(function () { showCopyFeedback(button, 'Failed to copy', true); });
            } catch (err) {
                showCopyFeedback(button, 'Failed to copy', true);
            }
        };
    });

    document.querySelectorAll('.copy-atproto-btn').forEach(function (button) {
        button.onclick = function (e) {
            e.stopPropagation();
            var uri = this.getAttribute('data-uri');
            navigator.clipboard.writeText(uri).then(function () { showCopyFeedback(button, 'ATProto URI copied!', false); })
                .catch(function () { showCopyFeedback(button, 'Failed to copy', true); });
        };
    });

    function showCopyFeedback(button, message, isError) {
        if (!button) return;
        var original = button.innerHTML;
        button.innerHTML = '<span class="dropdown-icon">' + (isError ? '╳' : '✓') + '</span> ' + message;
        button.disabled = true;
        setTimeout(function () { button.innerHTML = original; button.disabled = false; }, 2000);
    }
}
