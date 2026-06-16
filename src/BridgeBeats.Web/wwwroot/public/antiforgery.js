// Shared antiforgery token utility for all POST/PUT/PATCH/DELETE requests.
// Automatically fetches a token from /account/antiforgery-token and includes it
// as X-XSRF-TOKEN header. The raw token is never exposed outside this closure.

(function () {
    'use strict';

    var cachedToken = null;
    var tokenPromise = null;

    function getToken() {
        if (cachedToken) return Promise.resolve( cachedToken );
        if (tokenPromise) return tokenPromise;

        tokenPromise = fetch( '/account/antiforgery-token', { credentials: 'same-origin' } )
            .then( function ( response ) {
                if (!response.ok) {
                    throw new Error( 'Failed to fetch antiforgery token: ' + response.status );
                }
                return response.json();
            } )
            .then( function ( data ) {
                cachedToken = data.token;
                tokenPromise = null;
                return cachedToken;
            } )
            .catch( function ( err ) {
                tokenPromise = null;
                throw err;
            } );

        return tokenPromise;
    }

    /**
     * Drop-in replacement for fetch() that automatically adds the antiforgery
     * token header for unsafe HTTP methods (POST, PUT, PATCH, DELETE).
     * GET/HEAD/OPTIONS requests pass through to native fetch unchanged.
     *
     * @param {string|Request} url  The URL or Request object.
     * @param {object} [options]    Standard fetch options.
     * @returns {Promise<Response>} The fetch response.
     */
    window.safeFetch = function ( url, options ) {
        options = options || {};
        var method = (options.method || 'GET').toUpperCase();

        if (method !== 'POST' && method !== 'PUT' && method !== 'PATCH' && method !== 'DELETE') {
            return fetch( url, options );
        }

        if (!options.credentials) {
            options.credentials = 'same-origin';
        }

        return getToken().then( function ( token ) {
            options.headers = options.headers || {};

            if (options.headers instanceof Headers) {
                if (!options.headers.has( 'X-XSRF-TOKEN' )) {
                    options.headers.set( 'X-XSRF-TOKEN', token );
                }
            } else {
                if (!options.headers['X-XSRF-TOKEN']) {
                    options.headers['X-XSRF-TOKEN'] = token;
                }
            }

            return fetch( url, options );
        } );
    };
})();
