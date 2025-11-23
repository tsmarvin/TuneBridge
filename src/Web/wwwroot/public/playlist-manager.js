// Playlist management for TuneBridge
// Allows users to select multiple cards and create playlists

(function() {
    'use strict';

    // Playlist state
    const playlistState = {
        selectedCards: new Set(),
        maxCards: 20,
        isSelectionMode: false,
        isAuthenticated: false
    };

    // Check authentication status on load
    async function checkAuthStatus() {
        try {
            const response = await fetch('/account/status', {
                credentials: 'same-origin'
            });
            if (response.ok) {
                const data = await response.json();
                playlistState.isAuthenticated = data.isAuthenticated || false;
            }
        } catch (error) {
            console.error('Failed to check auth status:', error);
            playlistState.isAuthenticated = false;
        }
    }

    // Initialize playlist toolbar
    function initializePlaylistToolbar() {
        // Check if toolbar already exists
        if (document.getElementById('playlistToolbar')) {
            return;
        }

        const toolbar = document.createElement('div');
        toolbar.id = 'playlistToolbar';
        toolbar.className = 'playlist-toolbar';
        
        // Build toolbar HTML based on auth status
        let toolbarHTML = `
            <div class="playlist-toolbar-content">
                <div class="playlist-selection-info">
                    <span id="selectionCount">0</span> / ${playlistState.maxCards} selected
                </div>
                <div class="playlist-inputs`;
        
        if (playlistState.isAuthenticated) {
            toolbarHTML += `">
                    <input type="text" id="playlistTitleInput" class="form-control form-control-sm mb-2" placeholder="Playlist name (optional)" maxlength="100" />
                    <textarea id="playlistDescriptionInput" class="form-control form-control-sm" placeholder="Description (optional)" maxlength="500" rows="2"></textarea>`;
        } else {
            toolbarHTML += ` readonly">
                    <input type="text" id="playlistTitleInput" class="form-control form-control-sm" placeholder="TuneBridge" maxlength="100" readonly />
                    <small class="text-muted">Sign in to customize title and description</small>`;
        }
        
        toolbarHTML += `
                </div>
                <div class="playlist-actions">
                    <button id="createPlaylistBtn" class="btn btn-primary btn-sm" disabled>
                        Create Playlist
                    </button>
                    <button id="cancelSelectionBtn" class="btn btn-secondary btn-sm">
                        Cancel
                    </button>
                </div>
            </div>
        `;
        
        toolbar.innerHTML = toolbarHTML;

        // Insert at the top of results section
        const resultsSection = document.getElementById('resultsSection');
        if (resultsSection) {
            resultsSection.insertBefore(toolbar, resultsSection.firstChild);
        }

        // Add event listeners
        document.getElementById('createPlaylistBtn')?.addEventListener('click', createPlaylist);
        document.getElementById('cancelSelectionBtn')?.addEventListener('click', exitSelectionMode);
    }

    // Enter selection mode
    async function enterSelectionMode() {
        if (playlistState.isSelectionMode) return;

        // Check auth status before entering selection mode
        await checkAuthStatus();

        playlistState.isSelectionMode = true;
        playlistState.selectedCards.clear();

        // Hide the start button
        const startButton = document.getElementById('startPlaylistBtn');
        if (startButton) {
            startButton.style.display = 'none';
        }

        // Initialize toolbar
        initializePlaylistToolbar();

        // Add selection checkboxes to all cards
        const cards = document.querySelectorAll('.embed-card');
        cards.forEach(card => addCheckboxToCard(card));

        // Update button state
        updateToolbar();

        // Add selection mode class to body for styling
        document.body.classList.add('playlist-selection-mode');
    }

    // Exit selection mode
    function exitSelectionMode() {
        if (!playlistState.isSelectionMode) return;

        playlistState.isSelectionMode = false;
        playlistState.selectedCards.clear();

        // Show the start button again
        const startButton = document.getElementById('startPlaylistBtn');
        if (startButton) {
            startButton.style.display = '';
        }

        // Remove toolbar
        const toolbar = document.getElementById('playlistToolbar');
        if (toolbar) {
            toolbar.remove();
        }

        // Remove checkboxes from cards
        document.querySelectorAll('.card-selection-checkbox').forEach(cb => cb.remove());

        // Remove selected class from cards
        document.querySelectorAll('.embed-card.selected').forEach(card => {
            card.classList.remove('selected');
        });

        // Remove selection mode class
        document.body.classList.remove('playlist-selection-mode');
    }

    // Add checkbox to a card
    function addCheckboxToCard(card) {
        // Skip if checkbox already exists
        if (card.querySelector('.card-selection-checkbox')) return;

        // Extract card ID from share button or ATProto button
        const cardUrl = card.querySelector('.copy-link-btn')?.getAttribute('data-url');
        if (!cardUrl) return;

        const cardId = cardUrl.split('/').pop();
        if (!cardId) return;

        // Extract rkey from data attribute if available, otherwise fallback to cardId
        let rkey = card.getAttribute('data-rkey') || cardId;

        // Create checkbox overlay
        const checkbox = document.createElement('div');
        checkbox.className = 'card-selection-checkbox';
        
        // Create input element safely
        const input = document.createElement('input');
        input.type = 'checkbox';
        input.id = `select-${cardId}`;
        input.setAttribute('data-card-id', cardId);
        input.setAttribute('data-rkey', rkey);
        
        // Create label element safely
        const label = document.createElement('label');
        label.setAttribute('for', `select-${cardId}`);
        
        // Append elements
        checkbox.appendChild(input);
        checkbox.appendChild(label);

        // Insert at the beginning of the card
        card.insertBefore(checkbox, card.firstChild);

        // Add change event listener to checkbox input
        input.addEventListener('change', (e) => {
            handleCardSelection(e.target.checked, cardId, rkey, card);
        });

        // Prevent clicks on the checkbox from bubbling to card click handler
        checkbox.addEventListener('click', (e) => {
            e.stopPropagation();
        });

        // Add click handler to card for easier selection (but not on links/buttons/images)
        card.addEventListener('click', (e) => {
            // Don't toggle if clicking on links, buttons, or the checkbox itself
            // Images are now included for selection
            if (e.target.closest('button, .share-dropdown, .card-selection-checkbox')) return;
            
            // If clicking a link, check if we should select instead
            if (e.target.closest('a')) {
                // Prevent default link behavior in selection mode
                e.preventDefault();
                e.stopPropagation();
            }

            input.checked = !input.checked;
            input.dispatchEvent(new Event('change'));
        });

        // Make images clickable for selection (override the link prevention)
        const images = card.querySelectorAll('a img, img');
        images.forEach(img => {
            const link = img.closest('a');
            if (link) {
                link.addEventListener('click', (e) => {
                    // In selection mode, prevent navigation and toggle selection
                    e.preventDefault();
                    e.stopPropagation();
                    input.checked = !input.checked;
                    input.dispatchEvent(new Event('change'));
                });
                link.style.cursor = 'pointer'; // Keep pointer cursor for card selection
            } else {
                // For images without links, make them clickable too
                img.style.cursor = 'pointer';
                img.addEventListener('click', (e) => {
                    e.stopPropagation();
                    input.checked = !input.checked;
                    input.dispatchEvent(new Event('change'));
                });
            }
        });
    }

    // Handle card selection
    function handleCardSelection(selected, cardId, rkey, cardElement) {
        if (selected) {
            if (playlistState.selectedCards.size >= playlistState.maxCards) {
                // Max limit reached
                const input = cardElement.querySelector('input[type="checkbox"]');
                if (input) input.checked = false;
                
                // Show feedback in toolbar
                const toolbar = document.getElementById('playlistToolbar');
                if (toolbar) {
                    toolbar.classList.add('shake-animation');
                    setTimeout(() => toolbar.classList.remove('shake-animation'), 500);
                }
                return;
            }
            playlistState.selectedCards.add(JSON.stringify({ cardId, rkey }));
            cardElement.classList.add('selected');
        } else {
            // Find and remove the matching entry
            for (const entry of playlistState.selectedCards) {
                const parsed = JSON.parse(entry);
                if (parsed.cardId === cardId) {
                    playlistState.selectedCards.delete(entry);
                    break;
                }
            }
            cardElement.classList.remove('selected');
        }

        updateToolbar();
    }

    // Update toolbar display
    function updateToolbar() {
        const countEl = document.getElementById('selectionCount');
        const createBtn = document.getElementById('createPlaylistBtn');

        if (countEl) {
            countEl.textContent = playlistState.selectedCards.size;
        }

        if (createBtn) {
            createBtn.disabled = playlistState.selectedCards.size === 0;
        }
    }

    // Create playlist from selected cards
    async function createPlaylist() {
        if (playlistState.selectedCards.size === 0) return;

        const createBtn = document.getElementById('createPlaylistBtn');
        const titleInput = document.getElementById('playlistTitleInput');
        const descriptionInput = document.getElementById('playlistDescriptionInput');
        const originalText = createBtn?.textContent || 'Create Playlist';

        try {
            // Disable button and show loading state
            if (createBtn) {
                createBtn.disabled = true;
                createBtn.textContent = 'Creating...';
            }

            // Get title and description from input fields
            const title = titleInput?.value?.trim() || 'TuneBridge';
            const description = descriptionInput?.value?.trim() || '';

            // Parse selected cards to extract cardIds and rkeys
            const cardIds = [];
            const cardRkeys = [];
            for (const entry of playlistState.selectedCards) {
                const parsed = JSON.parse(entry);
                cardIds.push(parsed.cardId);
                cardRkeys.push(parsed.rkey);
            }

            // Create playlist via API
            const response = await fetch('/playlist/create', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                credentials: 'same-origin',
                body: JSON.stringify({
                    cardIds: cardIds,
                    cardRkeys: cardRkeys,
                    title: title,
                    description: description
                })
            });

            if (!response.ok) {
                const error = await response.json().catch(() => ({ error: 'Failed to create playlist' }));
                throw new Error(error.error || 'Failed to create playlist');
            }

            const data = await response.json();

            // Show success feedback and copy link
            if (data.playlistUrl) {
                // Copy to clipboard
                try {
                    await navigator.clipboard.writeText(data.playlistUrl);
                    
                    // Show success state on button
                    if (createBtn) {
                        createBtn.textContent = '✓ Link Copied!';
                        createBtn.classList.add('btn-success');
                        createBtn.classList.remove('btn-primary');
                        
                        // Wait a moment before exiting
                        setTimeout(() => {
                            exitSelectionMode();
                        }, 1500);
                    }
                } catch {
                    // Fallback if clipboard API fails - just show success
                    if (createBtn) {
                        createBtn.textContent = '✓ Playlist Created!';
                    }
                    setTimeout(() => {
                        exitSelectionMode();
                    }, 1500);
                }
            }

        } catch (error) {
            console.error('Error creating playlist:', error);
            
            // Show error state
            if (createBtn) {
                createBtn.textContent = '✗ Failed';
                createBtn.classList.add('btn-danger');
                createBtn.classList.remove('btn-primary');
                
                setTimeout(() => {
                    createBtn.textContent = originalText;
                    createBtn.classList.remove('btn-danger');
                    createBtn.classList.add('btn-primary');
                    createBtn.disabled = playlistState.selectedCards.size === 0;
                }, 2000);
            }
        }
    }

    // Add "Create Playlist" button to results section
    function addPlaylistButton() {
        const resultsSection = document.getElementById('resultsSection');
        if (!resultsSection || document.getElementById('startPlaylistBtn')) {
            return; // Already exists or no results section
        }

        // Check if there are any results
        const cards = resultsSection.querySelectorAll('.embed-card');
        if (cards.length === 0) {
            return;
        }

        // Create button
        const button = document.createElement('button');
        button.id = 'startPlaylistBtn';
        button.className = 'btn btn-outline-primary btn-sm mb-3';
        button.textContent = 'Create Playlist from Results';
        button.addEventListener('click', enterSelectionMode);

        // Insert before results
        const resultsHeading = resultsSection.querySelector('h3');
        if (resultsHeading) {
            resultsHeading.parentNode.insertBefore(button, resultsHeading.nextSibling);
        }
    }

    // Initialize on page load
    function init() {
        // Add playlist button when results are loaded
        // Use MutationObserver to detect when results are added
        const observer = new MutationObserver((mutations) => {
            for (const mutation of mutations) {
                if (mutation.addedNodes.length > 0) {
                    addPlaylistButton();
                }
            }
        });

        const resultsSection = document.getElementById('resultsSection');
        if (resultsSection) {
            observer.observe(resultsSection, { childList: true, subtree: true });
        }

        // Check immediately in case results are already loaded
        addPlaylistButton();
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Export functions for external use
    window.PlaylistManager = {
        enterSelectionMode,
        exitSelectionMode
    };

})();
