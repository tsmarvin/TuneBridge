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
        button.onclick = function (e) {
            e.stopPropagation();
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
    document.querySelectorAll('.copy-embed-btn').forEach(function (button) {
        button.onclick = function (e) {
            e.stopPropagation();
            console.log('Copy embed button clicked');
            
            try {
                var url = this.getAttribute('data-url');
                var titleAttr = this.getAttribute('data-title');
                console.log('URL:', url, 'Title attr:', titleAttr);
                
                var title;
                try {
                    title = JSON.parse(titleAttr);
                } catch (parseError) {
                    console.warn('Failed to parse title as JSON, using raw value:', parseError);
                    title = titleAttr;
                }
                
                // Convert card URL to embed URL by appending /embed
                var embedUrl = url;
                if (!embedUrl.endsWith('/embed')) {
                    embedUrl = embedUrl + '/embed';
                }
                
                var absoluteUrl = new URL(embedUrl, window.location.origin).href;
                // Card dimensions: width matches max-width of card (515px), height adjusted for compact card (~250px)
                var iframeCode = '<iframe src="' + absoluteUrl + '" width="515" height="250" frameborder="0" allowtransparency="true" style="max-width: 100%;" title="' + title + '"></iframe>';
                
                console.log('Generated iframe code:', iframeCode);

                navigator.clipboard.writeText(iframeCode).then(function () {
                    console.log('Successfully copied to clipboard');
                    showCopyFeedback(button, 'Embed code copied!', false);
                }).catch(function (err) {
                    console.error('Failed to copy embed code:', err);
                    showCopyFeedback(button, 'Failed to copy', true);
                });
            } catch (err) {
                console.error('Error in copy embed handler:', err);
                showCopyFeedback(button, 'Failed to copy', true);
            }
        };
    });

    // Handle copy ATProto URI buttons
    document.querySelectorAll('.copy-atproto-btn').forEach(function (button) {
        button.onclick = function (e) {
            e.stopPropagation();
            var uri = this.getAttribute('data-uri');

            navigator.clipboard.writeText(uri).then(function () {
                showCopyFeedback(button, 'ATProto URI copied!', false);
            }).catch(function (err) {
                console.error('Failed to copy ATProto URI:', err);
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
