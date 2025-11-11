// Share button functionality for music lookup results
// This file handles share buttons that are dynamically loaded via AJAX

function initializeShareButtons() {
    console.log('Initializing share buttons...');

    // Handle dropdown toggle
    document.querySelectorAll('.share-button').forEach(function (button) {
        button.onclick = function (e) {
            e.preventDefault();
            e.stopPropagation();

            console.log('Share button clicked');

            var dropdown = this.parentElement;
            var menu = dropdown.querySelector('.dropdown-menu');
            var isOpen = dropdown.classList.contains('show');

            // Close all other dropdowns
            document.querySelectorAll('.share-dropdown.show').forEach(function (openDropdown) {
                openDropdown.classList.remove('show');
                var openMenu = openDropdown.querySelector('.dropdown-menu');
                if (openMenu) openMenu.classList.remove('show');
                var openButton = openDropdown.querySelector('.share-button');
                if (openButton) openButton.setAttribute('aria-expanded', 'false');
            });

            // Toggle current dropdown
            if (!isOpen) {
                dropdown.classList.add('show');
                menu.classList.add('show');
                this.setAttribute('aria-expanded', 'true');
                console.log('Dropdown opened');
            }
        };
    });

    // Close dropdown when clicking outside
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

    // Handle copy link buttons
    document.querySelectorAll('.copy-link-btn').forEach(function (button) {
        button.onclick = function () {
            var url = this.getAttribute('data-url');
            var absoluteUrl = new URL(url, window.location.origin).href;

            navigator.clipboard.writeText(absoluteUrl).then(function () {
                showCopyFeedback(button, 'Link copied!', false);
            }).catch(function (err) {
                console.error('Failed to copy link:', err);
                showCopyFeedback(button, 'Failed to copy', true);
            });
        };
    });

    // Handle copy iframe buttons
    document.querySelectorAll('.copy-iframe-btn').forEach(function (button) {
        button.onclick = function () {
            var url = this.getAttribute('data-url');
            var title = JSON.parse(this.getAttribute('data-title'));
            var absoluteUrl = new URL(url, window.location.origin).href;
            var iframeCode = '<iframe src="' + absoluteUrl + '" width="600" height="600" frameborder="0" title="' + title + '"></iframe>';

            navigator.clipboard.writeText(iframeCode).then(function () {
                showCopyFeedback(button, 'Embed code copied!', false);
            }).catch(function (err) {
                console.error('Failed to copy embed code:', err);
                showCopyFeedback(button, 'Failed to copy', true);
            });
        };
    });

    function showCopyFeedback(button, message, isError) {
        if (!button) return;
        var originalText = button.innerHTML;
        var iconSpan = button.querySelector('.dropdown-icon');
        var icon = isError ? '╳' : '✓';

        if (iconSpan) {
            iconSpan.textContent = icon;
        }
        button.innerHTML = '<span class="dropdown-icon">' + icon + '</span> ' + message;
        button.disabled = true;

        setTimeout(function () {
            button.innerHTML = originalText;
            button.disabled = false;
        }, 2000);
    }

    console.log('Share buttons initialized');
}
