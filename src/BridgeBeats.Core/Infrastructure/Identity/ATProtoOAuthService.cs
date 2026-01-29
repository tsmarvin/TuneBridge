using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using idunno.AtProto;
using idunno.Bluesky;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Service for managing ATProto OAuth authentication flows.
/// Implements the ATProto OAuth profile with PAR, PKCE, and DPoP.
/// </summary>
public class ATProtoOAuthService : IATProtoOAuthService {

    /// <summary>
    /// Safety margin before token expiration to trigger refresh (30 seconds).
    /// </summary>
    private static readonly TimeSpan s_tokenExpirationMargin = TimeSpan.FromSeconds( 30 );

    /// <summary>
    /// How long to cache authorization server metadata (1 hour).
    /// </summary>
    private static readonly TimeSpan s_metadataCacheDuration = TimeSpan.FromHours(1);

    /// <summary>
    /// OAuth scopes required for playlist operations.
    /// - atproto: Required for all ATProto OAuth
    /// - repo:link.bridgebeats.playlist: Granular permission for playlist record CRUD only
    /// </summary>
    private static readonly string[] s_requiredScopes = ["atproto", "repo:link.bridgebeats.playlist"];

    /// <summary>
    /// The relative path to the OAuth callback endpoint.
    /// </summary>
    private const string OAuthCallbackPath = "/account/atproto-callback";

    /// <summary>
    /// The well-known path for client metadata.
    /// </summary>
    private const string ClientMetadataPath = "/.well-known/client-metadata.json";

    /// <summary>
    /// The well-known path for authorization server metadata.
    /// </summary>
    private const string AuthServerMetadataPath = "/.well-known/oauth-authorization-server";

    /// <summary>
    /// Maximum number of retry attempts for DPoP nonce errors.
    /// </summary>
    private const int MaxDPoPNonceRetries = 2;

    /// <summary>
    /// Cache for authorization server metadata to avoid repeated discovery requests.
    /// Key: Authorization server URI (normalized), Value: (metadata, expiration time).
    /// </summary>
    private static readonly ConcurrentDictionary<string, (AuthorizationServerMetadata Metadata, DateTime ExpiresAt)> s_metadataCache = new();

    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<ATProtoOAuthService> _logger;
    private readonly string _clientId;
    private readonly string _baseUrl;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ATProtoOAuthService"/> class.
    /// </summary>
    /// <param name="dbContextFactory">Factory for creating database contexts.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="clientId">The OAuth client_id URL (must be the client-metadata.json URL).</param>
    /// <param name="httpClientFactory">HTTP client factory for creating clients for token endpoint requests.</param>
    public ATProtoOAuthService(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<ATProtoOAuthService> logger,
        string clientId,
        IHttpClientFactory httpClientFactory
    ) {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException( nameof( dbContextFactory ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
        _clientId = clientId ?? throw new ArgumentNullException( nameof( clientId ) );
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException( nameof( httpClientFactory ) );

        // Extract base URL from client ID
        if (!clientId.EndsWith( ClientMetadataPath, StringComparison.OrdinalIgnoreCase )) {
            throw new ArgumentException( $"Client ID must end with '{ClientMetadataPath}'", nameof( clientId ) );
        }
        _baseUrl = clientId[..^ClientMetadataPath.Length];
    }

    /// <inheritdoc/>
    public async Task<(Uri AuthorizationUrl, string State)> StartAuthorizationAsync(
        string handle,
        Uri redirectUri,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( handle );
        ArgumentNullException.ThrowIfNull( redirectUri );

        // Normalize handle (remove @ prefix if present)
        handle = handle.TrimStart( '@' ).Trim( );

        _logger.LogInformation( "Starting ATProto OAuth flow for handle: {Handle}", handle );

        // Create an agent to resolve the handle and build the OAuth URL
        using BlueskyAgent agent = new( );

        // Step 1: Resolve handle to DID
        Did? did = await agent.ResolveHandle( handle, cancellationToken );
        if (did is null) {
            throw new InvalidOperationException( $"Failed to resolve handle '{handle}' to a DID" );
        }

        _logger.LogDebug( "Resolved handle {Handle} to DID {Did}", handle, did );

        // Step 2: Resolve PDS URI
        Uri? pdsUri = await agent.ResolvePds( did, cancellationToken );
        if (pdsUri is null) {
            throw new InvalidOperationException( $"Failed to resolve PDS for DID '{did}'" );
        }

        _logger.LogDebug( "Resolved DID {Did} to PDS {PdsUri}", did, pdsUri );

        // Step 3: Resolve authorization server
        Uri? authorizationServer = await agent.ResolveAuthorizationServer( pdsUri, cancellationToken );
        if (authorizationServer is null) {
            throw new InvalidOperationException( $"Failed to resolve authorization server for PDS '{pdsUri}'" );
        }

        _logger.LogDebug( "Resolved PDS {PdsUri} to authorization server {AuthServer}", pdsUri, authorizationServer );

        // Step 4: Fetch authorization server metadata
        AuthorizationServerMetadata metadata = await GetAuthorizationServerMetadataAsync(
            authorizationServer,
            cancellationToken
        );

        _logger.LogDebug(
            "Fetched auth server metadata. Token endpoint: {TokenEndpoint}, PAR endpoint: {ParEndpoint}",
            metadata.TokenEndpoint,
            metadata.PushedAuthorizationRequestEndpoint
        );

        // Step 5: Generate PKCE code verifier and challenge
        string codeVerifier = GenerateCodeVerifier( );
        string codeChallenge = GenerateCodeChallenge( codeVerifier );

        // Step 6: Generate state parameter
        string state = GenerateState( );

        // Step 7: Generate DPoP key pair
        string dpoPKeyJwk = GenerateDPoPKey( );

        // Step 8: Store the OAuth state for later verification
        await using ApplicationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync( cancellationToken );
        AtProtoOAuthState oauthState = new( ) {
            State = state,
            CodeVerifier = codeVerifier,
            Handle = handle,
            Did = did.ToString( ),
            PdsUri = pdsUri.ToString( ),
            AuthorizationServerUri = authorizationServer.ToString( ),
            DPoPKeyJwk = dpoPKeyJwk,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes( 5 )
        };

        _ = dbContext.AtProtoOAuthStates.Add( oauthState );
        _ = await dbContext.SaveChangesAsync( cancellationToken );

        // Step 9: Use PAR (Pushed Authorization Request) if available (required by ATProto spec)
        Uri authorizationUrl;
        if (metadata.PushedAuthorizationRequestEndpoint is not null) {
            authorizationUrl = await PerformPushedAuthorizationRequestAsync(
                metadata,
                dpoPKeyJwk,
                redirectUri,
                state,
                codeChallenge,
                handle,
                cancellationToken
            );
        } else {
            // Fallback to direct authorization URL (may not work with all ATProto servers)
            _logger.LogWarning(
                "Authorization server {AuthServer} does not support PAR. Using direct authorization URL.",
                authorizationServer
            );
            authorizationUrl = BuildDirectAuthorizationUrl(
                metadata.AuthorizationEndpoint!,
                authorizationServer,
                redirectUri,
                state,
                codeChallenge,
                handle
            );
        }

        _logger.LogInformation(
            "OAuth authorization started for handle {Handle}, state {State}",
            handle,
            state
        );

        return (authorizationUrl, state);
    }

    /// <inheritdoc/>
    public async Task<ATProtoOAuthResult> CompleteAuthorizationAsync(
        string state,
        string code,
        string iss,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( state );
        ArgumentException.ThrowIfNullOrWhiteSpace( code );
        ArgumentException.ThrowIfNullOrWhiteSpace( iss );

        _logger.LogInformation( "Completing ATProto OAuth flow for state: {State}", state );

        // Step 1: Look up the OAuth state
        await using ApplicationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync( cancellationToken );
        AtProtoOAuthState? oauthState = await dbContext.AtProtoOAuthStates
            .FirstOrDefaultAsync( s => s.State == state, cancellationToken );

        if (oauthState is null) {
            throw new InvalidOperationException( "OAuth state not found. The authorization flow may have expired." );
        }

        if (oauthState.ExpiresAt < DateTime.UtcNow) {
            // Clean up expired state
            _ = dbContext.AtProtoOAuthStates.Remove( oauthState );
            _ = await dbContext.SaveChangesAsync( cancellationToken );
            throw new InvalidOperationException( "OAuth state has expired. Please try logging in again." );
        }

        // Step 2: Verify the issuer matches the expected authorization server
        if (!string.Equals( oauthState.AuthorizationServerUri?.TrimEnd( '/' ), iss.TrimEnd( '/' ), StringComparison.OrdinalIgnoreCase )) {
            _logger.LogWarning(
                "Issuer mismatch. Expected: {Expected}, Got: {Actual}",
                oauthState.AuthorizationServerUri,
                iss
            );
            throw new InvalidOperationException( "Authorization server issuer does not match. Possible security issue." );
        }

        // Step 3: Exchange the authorization code for tokens
        // Note: This requires calling the token endpoint with PKCE verifier and DPoP
        // For now, this is a placeholder - the actual implementation will use the idunno library
        // or direct HTTP calls to the token endpoint
        ATProtoOAuthResult result = await ExchangeCodeForTokensAsync(
            oauthState,
            code,
            cancellationToken
        );

        // Step 4: Clean up the OAuth state (single-use)
        _ = dbContext.AtProtoOAuthStates.Remove( oauthState );
        _ = await dbContext.SaveChangesAsync( cancellationToken );

        _logger.LogInformation(
            "OAuth authorization completed for DID {Did}, handle {Handle}",
            result.Did,
            result.Handle
        );

        return result;
    }

    /// <inheritdoc/>
    public async Task<ATProtoOAuthResult?> RefreshTokensAsync(
        string did,
        string refreshToken,
        string dpoPKeyJwk,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( did );
        ArgumentException.ThrowIfNullOrWhiteSpace( refreshToken );
        ArgumentException.ThrowIfNullOrWhiteSpace( dpoPKeyJwk );

        _logger.LogDebug( "Refreshing ATProto tokens for DID: {Did}", did );

        try {
            // Resolve the user's PDS to find the token endpoint
            using BlueskyAgent agent = new( );
            Uri? pdsUri = await agent.ResolvePds( new Did( did ), cancellationToken );
            if (pdsUri is null) {
                _logger.LogWarning( "Failed to resolve PDS for DID {Did} during token refresh", did );
                return null;
            }

            Uri? authServer = await agent.ResolveAuthorizationServer( pdsUri, cancellationToken );
            if (authServer is null) {
                _logger.LogWarning( "Failed to resolve auth server for DID {Did} during token refresh", did );
                return null;
            }

            // Call the token endpoint with refresh_token grant
            // Note: This is a placeholder - actual implementation needs DPoP-signed request
            ATProtoOAuthResult? result = await RefreshTokensFromServerAsync(
                authServer,
                refreshToken,
                dpoPKeyJwk,
                did,
                cancellationToken
            );

            if (result is not null) {
                _logger.LogInformation( "Successfully refreshed ATProto tokens for DID {Did}", did );
            }

            return result;
        } catch (Exception ex) {
            _logger.LogError( ex, "Failed to refresh ATProto tokens for DID {Did}", did );
            return null;
        }
    }

    /// <inheritdoc/>
    public bool IsTokenValid( DateTime? tokenExpiration ) {
        return tokenExpiration is not null && tokenExpiration.Value > DateTime.UtcNow.Add( s_tokenExpirationMargin );
    }

    /// <inheritdoc/>
    public async Task<int> CleanupExpiredStatesAsync( CancellationToken cancellationToken = default ) {
        await using ApplicationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync( cancellationToken );

        DateTime cutoff = DateTime.UtcNow;
        int deleted = await dbContext.AtProtoOAuthStates
            .Where( s => s.ExpiresAt < cutoff )
            .ExecuteDeleteAsync( cancellationToken );

        if (deleted > 0) {
            _logger.LogInformation( "Cleaned up {Count} expired ATProto OAuth states", deleted );
        }

        return deleted;
    }

    #region Private Helper Methods

    /// <summary>
    /// Generates a cryptographically random PKCE code verifier.
    /// </summary>
    private static string GenerateCodeVerifier( ) {
        byte[] bytes = new byte[32];
        RandomNumberGenerator.Fill( bytes );
        return Base64UrlEncode( bytes );
    }

    /// <summary>
    /// Generates a PKCE code challenge from the verifier using S256 method.
    /// </summary>
    private static string GenerateCodeChallenge( string codeVerifier ) {
        byte[] bytes = SHA256.HashData( Encoding.ASCII.GetBytes( codeVerifier ) );
        return Base64UrlEncode( bytes );
    }

    /// <summary>
    /// Generates a cryptographically random state parameter.
    /// </summary>
    private static string GenerateState( ) {
        byte[] bytes = new byte[32];
        RandomNumberGenerator.Fill( bytes );
        return Base64UrlEncode( bytes );
    }

    /// <summary>
    /// Generates a new EC P-256 DPoP key pair and returns the private key as JWK.
    /// </summary>
    private static string GenerateDPoPKey( ) {
        using ECDsa ecdsa = ECDsa.Create( ECCurve.NamedCurves.nistP256 );
        ECParameters parameters = ecdsa.ExportParameters( includePrivateParameters: true );

        var jwk = new {
            kty = "EC",
            crv = "P-256",
            x = Base64UrlEncode( parameters.Q.X! ),
            y = Base64UrlEncode( parameters.Q.Y! ),
            d = Base64UrlEncode( parameters.D! ),
            kid = Guid.NewGuid( ).ToString( "N" )
        };

        return JsonSerializer.Serialize( jwk );
    }

    /// <summary>
    /// Fetches and caches the authorization server metadata from the well-known endpoint.
    /// </summary>
    /// <param name="authorizationServer">The authorization server base URI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization server metadata.</returns>
    private async Task<AuthorizationServerMetadata> GetAuthorizationServerMetadataAsync(
        Uri authorizationServer,
        CancellationToken cancellationToken
    ) {
        string cacheKey = authorizationServer.ToString().TrimEnd('/').ToLowerInvariant();

        // Check cache first
        if (s_metadataCache.TryGetValue( cacheKey, out (AuthorizationServerMetadata Metadata, DateTime ExpiresAt) cached ) && cached.ExpiresAt > DateTime.UtcNow) {
            _logger.LogDebug( "Using cached authorization server metadata for {AuthServer}", authorizationServer );
            return cached.Metadata;
        }

        // Fetch metadata from well-known endpoint
        Uri metadataUrl = new(authorizationServer, AuthServerMetadataPath);
        _logger.LogDebug( "Fetching authorization server metadata from {MetadataUrl}", metadataUrl );

        HttpClient httpClient = _httpClientFactory.CreateClient("ATProtoOAuth");
        using HttpResponseMessage response = await httpClient.GetAsync(metadataUrl, cancellationToken);

        if (!response.IsSuccessStatusCode) {
            string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Failed to fetch authorization server metadata from {MetadataUrl}. Status: {StatusCode}. Response: {Response}",
                metadataUrl,
                response.StatusCode,
                errorContent.Length > 200 ? errorContent[..200] + "..." : errorContent
            );
            throw new InvalidOperationException( $"Failed to fetch authorization server metadata: {response.StatusCode}" );
        }

        string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        AuthorizationServerMetadata metadata;

        try {
            using JsonDocument doc = JsonDocument.Parse(responseContent);
            JsonElement root = doc.RootElement;

            metadata = new AuthorizationServerMetadata {
                Issuer = root.TryGetProperty( "issuer", out JsonElement issuerElem ) ? issuerElem.GetString( ) : null,
                AuthorizationEndpoint = root.TryGetProperty( "authorization_endpoint", out JsonElement authEndpointElem )
                    ? new Uri( authEndpointElem.GetString( )! )
                    : null,
                TokenEndpoint = root.TryGetProperty( "token_endpoint", out JsonElement tokenEndpointElem )
                    ? new Uri( tokenEndpointElem.GetString( )! )
                    : null,
                PushedAuthorizationRequestEndpoint = root.TryGetProperty( "pushed_authorization_request_endpoint", out JsonElement parEndpointElem )
                    ? new Uri( parEndpointElem.GetString( )! )
                    : null,
                RequiresPushedAuthorizationRequests = root.TryGetProperty( "require_pushed_authorization_requests", out JsonElement requireParElem )
                    && requireParElem.GetBoolean( ),
                DPoPSigningAlgValuesSupported = root.TryGetProperty( "dpop_signing_alg_values_supported", out JsonElement dpopAlgsElem )
                    ? dpopAlgsElem.EnumerateArray( ).Select( e => e.GetString( )! ).ToArray( )
                    : []
            };
        } catch (JsonException ex) {
            _logger.LogError( ex, "Failed to parse authorization server metadata from {MetadataUrl}", metadataUrl );
            throw new InvalidOperationException( "Failed to parse authorization server metadata", ex );
        }

        // Cache the metadata
        s_metadataCache[cacheKey] = (metadata, DateTime.UtcNow.Add( s_metadataCacheDuration ));

        _logger.LogDebug(
            "Cached authorization server metadata for {AuthServer}. PAR required: {ParRequired}",
            authorizationServer,
            metadata.RequiresPushedAuthorizationRequests
        );

        return metadata;
    }

    /// <summary>
    /// Performs a Pushed Authorization Request (PAR) and returns the authorization URL.
    /// </summary>
    private async Task<Uri> PerformPushedAuthorizationRequestAsync(
        AuthorizationServerMetadata metadata,
        string dpoPKeyJwk,
        Uri redirectUri,
        string state,
        string codeChallenge,
        string loginHint,
        CancellationToken cancellationToken
    ) {
        if (metadata.PushedAuthorizationRequestEndpoint is null) {
            throw new InvalidOperationException( "Authorization server does not have a PAR endpoint" );
        }

        if (metadata.AuthorizationEndpoint is null) {
            throw new InvalidOperationException( "Authorization server metadata missing authorization_endpoint" );
        }

        string scope = string.Join(" ", s_requiredScopes);

        // Build PAR request parameters
        Dictionary<string, string> parFormData = new()
        {
            ["client_id"] = _clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri.ToString(),
            ["state"] = state,
            ["scope"] = scope,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["login_hint"] = loginHint
        };

        // Execute PAR request with DPoP
        (string responseContent, _) = await ExecuteTokenRequestWithDPoPRetryAsync(
            metadata.PushedAuthorizationRequestEndpoint,
            parFormData,
            dpoPKeyJwk,
            accessToken: null,
            cancellationToken
        );

        // Parse PAR response
        string requestUri;
        try {
            using JsonDocument doc = JsonDocument.Parse(responseContent);
            JsonElement root = doc.RootElement;

            requestUri = root.GetProperty( "request_uri" ).GetString( )
                ?? throw new InvalidOperationException( "PAR response missing request_uri" );
        } catch (JsonException ex) {
            _logger.LogError( ex, "Failed to parse PAR response" );
            throw new InvalidOperationException( "Failed to parse PAR response", ex );
        }

        _logger.LogDebug( "PAR successful. Request URI: {RequestUri}", requestUri );

        // Build authorization URL with request_uri
        UriBuilder builder = new(metadata.AuthorizationEndpoint);
        System.Collections.Specialized.NameValueCollection query = System.Web.HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = _clientId;
        query["request_uri"] = requestUri;
        builder.Query = query.ToString( );

        return builder.Uri;
    }

    /// <summary>
    /// Builds a direct OAuth authorization URL (fallback when PAR is not available).
    /// Includes the issuer parameter to prevent mix-up attacks per ATProto spec.
    /// </summary>
    private Uri BuildDirectAuthorizationUrl(
        Uri authorizationEndpoint,
        Uri authorizationServer,
        Uri redirectUri,
        string state,
        string codeChallenge,
        string loginHint
    ) {
        UriBuilder builder = new(authorizationEndpoint);

        string scope = string.Join( " ", s_requiredScopes );

        System.Collections.Specialized.NameValueCollection query = System.Web.HttpUtility.ParseQueryString( string.Empty );
        query["client_id"] = _clientId;
        query["response_type"] = "code";
        query["redirect_uri"] = redirectUri.ToString( );
        query["state"] = state;
        query["scope"] = scope;
        query["code_challenge"] = codeChallenge;
        query["code_challenge_method"] = "S256";
        query["login_hint"] = loginHint;
        // Include issuer to prevent OAuth mix-up attacks (per ATProto spec recommendation)
        query["iss"] = authorizationServer.ToString( ).TrimEnd( '/' );

        builder.Query = query.ToString( );

        return builder.Uri;
    }

    /// <summary>
    /// Executes a token endpoint request with DPoP and automatic nonce retry logic.
    /// </summary>
    /// <param name="endpoint">The token endpoint URI.</param>
    /// <param name="formData">The form data to send.</param>
    /// <param name="dpoPKeyJwk">The DPoP private key in JWK format.</param>
    /// <param name="accessToken">Optional access token for DPoP binding.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response content and the last DPoP nonce received (if any).</returns>
    private async Task<(string ResponseContent, string? DPoPNonce)> ExecuteTokenRequestWithDPoPRetryAsync(
        Uri endpoint,
        Dictionary<string, string> formData,
        string dpoPKeyJwk,
        string? accessToken,
        CancellationToken cancellationToken
    ) {
        HttpClient httpClient = _httpClientFactory.CreateClient("ATProtoOAuth");
        string? currentNonce = null;

        for (int attempt = 0; attempt <= MaxDPoPNonceRetries; attempt++) {
            // Create DPoP proof with current nonce (null on first attempt)
            string dpopProof = CreateDPoPProof(
                dpoPKeyJwk,
                "POST",
                endpoint.ToString(),
                accessToken,
                currentNonce
            );

            using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(formData)
            };
            request.Headers.Add( "DPoP", dpopProof );

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            // Check for DPoP nonce requirement
            if (response.StatusCode == HttpStatusCode.BadRequest) {
                string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);

                // Check if this is a use_dpop_nonce error
                if (errorContent.Contains( "use_dpop_nonce", StringComparison.OrdinalIgnoreCase ) &&
                    response.Headers.TryGetValues( "DPoP-Nonce", out IEnumerable<string>? nonceValues )) {
                    currentNonce = nonceValues.FirstOrDefault( );
                    if (!string.IsNullOrWhiteSpace( currentNonce ) && attempt < MaxDPoPNonceRetries) {
                        _logger.LogDebug(
                            "Received use_dpop_nonce error, retrying with nonce. Attempt {Attempt}/{MaxRetries}",
                            attempt + 1,
                            MaxDPoPNonceRetries
                        );
                        continue;
                    }
                }

                // Not a nonce error or max retries exceeded
                _logger.LogError(
                    "Token request failed with status {StatusCode}. Response: {Response}",
                    response.StatusCode,
                    errorContent.Length > 200 ? errorContent[..200] + "..." : errorContent
                );
                throw new InvalidOperationException( $"Token request failed: {response.StatusCode}. Response: {errorContent}" );
            }

            if (!response.IsSuccessStatusCode) {
                string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Token request failed with status {StatusCode}. Response: {Response}",
                    response.StatusCode,
                    errorContent.Length > 200 ? errorContent[..200] + "..." : errorContent
                );
                throw new InvalidOperationException( $"Token request failed: {response.StatusCode}" );
            }

            // Success - extract nonce from response headers if present for future use
            if (response.Headers.TryGetValues( "DPoP-Nonce", out IEnumerable<string>? respNonceValues )) {
                currentNonce = respNonceValues.FirstOrDefault( );
            }

            string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return (responseContent, currentNonce);
        }

        throw new InvalidOperationException( "Token request failed after maximum DPoP nonce retries" );
    }

    /// <summary>
    /// Exchanges the authorization code for tokens.
    /// </summary>
    private async Task<ATProtoOAuthResult> ExchangeCodeForTokensAsync(
        AtProtoOAuthState oauthState,
        string code,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull( oauthState );
        ArgumentException.ThrowIfNullOrWhiteSpace( code );

        _logger.LogInformation(
            "Exchanging authorization code for tokens. State: {State}",
            oauthState.State
        );

        // Step 1: Fetch authorization server metadata to get the token endpoint
        Uri authServer = new(oauthState.AuthorizationServerUri!);
        AuthorizationServerMetadata metadata = await GetAuthorizationServerMetadataAsync(
            authServer,
            cancellationToken
        );

        if (metadata.TokenEndpoint is null) {
            throw new InvalidOperationException( "Authorization server metadata missing token_endpoint" );
        }

        // Step 2: Build the token request parameters
        Dictionary<string, string> formData = new( ) {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = _clientId,
            ["redirect_uri"] = $"{_baseUrl}{OAuthCallbackPath}",
            ["code_verifier"] = oauthState.CodeVerifier!
        };

        // Step 3: Execute token request with DPoP nonce retry logic
        (string responseContent, _) = await ExecuteTokenRequestWithDPoPRetryAsync(
            metadata.TokenEndpoint,
            formData,
            oauthState.DPoPKeyJwk!,
            accessToken: null,
            cancellationToken
        );

        string accessToken;
        string refreshToken;
        int expiresIn;
        string? scope;
        string? sub;

        try {
            using JsonDocument doc = JsonDocument.Parse( responseContent );
            JsonElement root = doc.RootElement;

            // GetProperty() throws KeyNotFoundException if property doesn't exist
            // GetString() returns null if property exists but is null/empty
            // The null coalescing operator (??) then throws InvalidOperationException
            accessToken = root.GetProperty( "access_token" ).GetString( )
                ?? throw new InvalidOperationException( "access_token missing from token response" );
            refreshToken = root.GetProperty( "refresh_token" ).GetString( )
                ?? throw new InvalidOperationException( "refresh_token missing from token response" );
            expiresIn = root.GetProperty( "expires_in" ).GetInt32( );
            scope = root.TryGetProperty( "scope", out JsonElement scopeElem ) ? scopeElem.GetString( ) : null;
            sub = root.TryGetProperty( "sub", out JsonElement subElem ) ? subElem.GetString( ) : null;
        } catch (JsonException ex) {
            _logger.LogError(
                ex,
                "Failed to parse token response. Content: {ResponseContent}",
                responseContent
            );
            throw new InvalidOperationException( "Token response had invalid format or missing required properties.", ex );
        } catch (KeyNotFoundException ex) {
            // This catches missing properties from GetProperty() calls above
            _logger.LogError(
                ex,
                "Token response missing required property. Content: {ResponseContent}",
                responseContent
            );
            throw new InvalidOperationException( "Token response missing required property.", ex );
        }

        // Step 6: Validate the sub (DID) - fail if missing or doesn't match
        if (string.IsNullOrWhiteSpace( sub )) {
            _logger.LogError(
                "Token response missing 'sub' (DID) claim. Content: {ResponseContent}",
                responseContent
            );
            throw new InvalidOperationException( "Token response missing 'sub' (DID) claim" );
        }

        if (!string.Equals( sub, oauthState.Did, StringComparison.OrdinalIgnoreCase )) {
            _logger.LogWarning(
                "DID mismatch in token response. Expected: {Expected}, Got: {Actual}",
                oauthState.Did,
                sub
            );
            throw new InvalidOperationException( "Token response DID does not match expected DID" );
        }

        _logger.LogInformation(
            "Successfully exchanged authorization code for tokens. DID: {Did}",
            sub
        );

        // Step 7: Return the OAuth result
        return new ATProtoOAuthResult {
            Did = sub,
            Handle = oauthState.Handle!,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            DPoPKeyJwk = oauthState.DPoPKeyJwk!,
            TokenExpiration = DateTime.UtcNow.AddSeconds( expiresIn ),
            Scope = scope ?? string.Join( " ", s_requiredScopes )
        };
    }

    /// <summary>
    /// Refreshes tokens from the authorization server.
    /// </summary>
    private async Task<ATProtoOAuthResult?> RefreshTokensFromServerAsync(
        Uri authorizationServer,
        string refreshToken,
        string dpoPKeyJwk,
        string did,
        CancellationToken cancellationToken
    ) {
        // Step 1: Fetch authorization server metadata to get the token endpoint
        AuthorizationServerMetadata metadata = await GetAuthorizationServerMetadataAsync(
            authorizationServer,
            cancellationToken
        );

        if (metadata.TokenEndpoint is null) {
            _logger.LogWarning( "Authorization server metadata missing token_endpoint for DID {Did}", did );
            return null;
        }

        // Step 2: Build the refresh token request parameters
        Dictionary<string, string> formData = new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _clientId
        };

        // Step 3: Execute token request with DPoP nonce retry logic
        (string responseContent, _) = await ExecuteTokenRequestWithDPoPRetryAsync(
            metadata.TokenEndpoint,
            formData,
            dpoPKeyJwk,
            accessToken: null,
            cancellationToken
        );

        // Step 4: Parse the token response
        string accessTokenResult;
        string refreshTokenResult;
        int expiresIn;
        string? scope;
        string? sub;

        try {
            using JsonDocument doc = JsonDocument.Parse(responseContent);
            JsonElement root = doc.RootElement;

            accessTokenResult = root.GetProperty( "access_token" ).GetString( )
                ?? throw new InvalidOperationException( "access_token missing from refresh response" );
            refreshTokenResult = root.GetProperty( "refresh_token" ).GetString( )
                ?? throw new InvalidOperationException( "refresh_token missing from refresh response" );
            expiresIn = root.GetProperty( "expires_in" ).GetInt32( );
            scope = root.TryGetProperty( "scope", out JsonElement scopeElem ) ? scopeElem.GetString( ) : null;
            sub = root.TryGetProperty( "sub", out JsonElement subElem ) ? subElem.GetString( ) : null;
        } catch (Exception ex) when (ex is JsonException or KeyNotFoundException) {
            _logger.LogError( ex, "Failed to parse token refresh response for DID {Did}", did );
            return null;
        }

        // Step 5: Validate the sub (DID) matches if present
        if (!string.IsNullOrWhiteSpace( sub ) && !string.Equals( sub, did, StringComparison.OrdinalIgnoreCase )) {
            _logger.LogWarning(
                "DID mismatch in token refresh response. Expected: {Expected}, Got: {Actual}",
                did,
                sub
            );
            return null;
        }

        _logger.LogInformation( "Successfully refreshed ATProto tokens for DID {Did}", did );

        // Step 6: Return the refreshed OAuth result
        return new ATProtoOAuthResult {
            Did = did,
            Handle = string.Empty, // Handle not returned in refresh response
            AccessToken = accessTokenResult,
            RefreshToken = refreshTokenResult,
            DPoPKeyJwk = dpoPKeyJwk,
            TokenExpiration = DateTime.UtcNow.AddSeconds( expiresIn ),
            Scope = scope ?? string.Join( " ", s_requiredScopes )
        };
    }

    /// <summary>
    /// Creates a DPoP proof JWT for the given HTTP method and URL.
    /// </summary>
    /// <param name="dpoPKeyJwk">The DPoP private key in JWK format.</param>
    /// <param name="httpMethod">The HTTP method (e.g., "POST", "GET").</param>
    /// <param name="url">The full URL of the request.</param>
    /// <param name="accessToken">Optional access token to bind in the proof.</param>
    /// <param name="nonce">Optional server-provided nonce for DPoP nonce binding.</param>
    /// <returns>The DPoP proof JWT.</returns>
    private static string CreateDPoPProof( string dpoPKeyJwk, string httpMethod, string url, string? accessToken, string? nonce = null ) {
        // Parse the JWK to get the EC key parameters
        using JsonDocument jwkDoc = JsonDocument.Parse( dpoPKeyJwk );
        JsonElement jwk = jwkDoc.RootElement;

        // Validate required JWK parameters
        if (!jwk.TryGetProperty( "kty", out JsonElement ktyProp ) || ktyProp.GetString( ) != "EC") {
            throw new ArgumentException( "DPoP key JWK must have 'kty' set to 'EC'.", nameof( dpoPKeyJwk ) );
        }
        if (!jwk.TryGetProperty( "crv", out JsonElement crvProp ) || crvProp.GetString( ) != "P-256") {
            throw new ArgumentException( "DPoP key JWK must have 'crv' set to 'P-256'.", nameof( dpoPKeyJwk ) );
        }
        if (!jwk.TryGetProperty( "x", out JsonElement xProp ) || string.IsNullOrWhiteSpace( xProp.GetString( ) )) {
            throw new ArgumentException( "DPoP key JWK does not contain required parameter 'x'.", nameof( dpoPKeyJwk ) );
        }
        if (!jwk.TryGetProperty( "y", out JsonElement yProp ) || string.IsNullOrWhiteSpace( yProp.GetString( ) )) {
            throw new ArgumentException( "DPoP key JWK does not contain required parameter 'y'.", nameof( dpoPKeyJwk ) );
        }
        if (!jwk.TryGetProperty( "d", out JsonElement dProp ) || string.IsNullOrWhiteSpace( dProp.GetString( ) )) {
            throw new ArgumentException( "DPoP key JWK does not contain required private key parameter 'd'.", nameof( dpoPKeyJwk ) );
        }
        if (!jwk.TryGetProperty( "kid", out JsonElement kidProp ) || string.IsNullOrWhiteSpace( kidProp.GetString( ) )) {
            throw new ArgumentException( "DPoP key JWK does not contain required parameter 'kid'.", nameof( dpoPKeyJwk ) );
        }

        string x = xProp.GetString( )!;
        string y = yProp.GetString( )!;
        string d = dProp.GetString( )!;
        string kid = kidProp.GetString( )!;

        // Create the ECDsa key from JWK parameters
        byte[] xBytes = Base64UrlDecode( x );
        byte[] yBytes = Base64UrlDecode( y );
        byte[] dBytes = Base64UrlDecode( d );

        using ECDsa ecdsa = ECDsa.Create( new ECParameters {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = xBytes, Y = yBytes },
            D = dBytes
        } );

        // Create the DPoP JWT
        JwtSecurityTokenHandler handler = new( );
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds( );
        DateTime notBefore = DateTimeOffset.FromUnixTimeSeconds( now - 5 ).UtcDateTime;
        SecurityTokenDescriptor descriptor = new( ) {
            Claims = new Dictionary<string, object> {
                ["htm"] = httpMethod,
                ["htu"] = url,
                ["jti"] = Guid.NewGuid( ).ToString( "N" ),
                ["iat"] = now
            },
            NotBefore = notBefore,
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey( ecdsa ) { KeyId = kid },
                SecurityAlgorithms.EcdsaSha256
            )
        };

        // Add access token hash if provided
        if (!string.IsNullOrWhiteSpace( accessToken )) {
            byte[] hash = SHA256.HashData( Encoding.ASCII.GetBytes( accessToken ) );
            descriptor.Claims["ath"] = Base64UrlEncode( hash );
        }

        // Add nonce if provided (required for DPoP nonce binding per RFC 9449)
        if (!string.IsNullOrWhiteSpace( nonce )) {
            descriptor.Claims["nonce"] = nonce;
        }

        // Set the token type and algorithm in the header
        descriptor.AdditionalHeaderClaims = new Dictionary<string, object> {
            ["typ"] = "dpop+jwt",
            ["alg"] = "ES256",
            ["jwk"] = new {
                kty = "EC",
                crv = "P-256",
                x = x,
                y = y
            }
        };

        // Create and write the token while ECDsa is still in scope
        SecurityToken token = handler.CreateToken( descriptor );
        return handler.WriteToken( token );
    }

    /// <summary>
    /// Base64 URL decodes a string.
    /// </summary>
    private static byte[] Base64UrlDecode( string base64Url ) {
        string padded = base64Url.PadRight( base64Url.Length + ((4 - (base64Url.Length % 4)) % 4), '=' );
        string base64 = padded.Replace( '-', '+' ).Replace( '_', '/' );
        return Convert.FromBase64String( base64 );
    }

    /// <summary>
    /// Base64 URL encodes the given bytes.
    /// </summary>
    private static string Base64UrlEncode( byte[] bytes ) {
        return Convert.ToBase64String( bytes )
            .TrimEnd( '=' )
            .Replace( '+', '-' )
            .Replace( '/', '_' );
    }

    #endregion
}

/// <summary>
/// Represents the metadata from an OAuth 2.0 authorization server's well-known endpoint.
/// </summary>
internal sealed record AuthorizationServerMetadata {
    /// <summary>
    /// The authorization server's issuer identifier.
    /// </summary>
    public string? Issuer { get; init; }

    /// <summary>
    /// The authorization endpoint URL.
    /// </summary>
    public Uri? AuthorizationEndpoint { get; init; }

    /// <summary>
    /// The token endpoint URL.
    /// </summary>
    public Uri? TokenEndpoint { get; init; }

    /// <summary>
    /// The pushed authorization request endpoint URL (PAR).
    /// </summary>
    public Uri? PushedAuthorizationRequestEndpoint { get; init; }

    /// <summary>
    /// Whether the server requires pushed authorization requests.
    /// </summary>
    public bool RequiresPushedAuthorizationRequests { get; init; }

    /// <summary>
    /// Supported DPoP signing algorithms.
    /// </summary>
    public string[] DPoPSigningAlgValuesSupported { get; init; } = [];
}
