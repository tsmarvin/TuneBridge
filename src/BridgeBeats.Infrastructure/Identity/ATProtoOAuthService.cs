using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using idunno.AtProto;
using idunno.Bluesky;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace BridgeBeats.Infrastructure.Identity;

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
    /// OAuth scopes required for playlist operations.
    /// - atproto: Required for all ATProto OAuth
    /// - repo:link.bridgebeats.playlist: Granular permission for playlist record CRUD only
    /// </summary>
    private static readonly string[] s_requiredScopes = ["atproto", "repo:link.bridgebeats.playlist"];

    /// <summary>
    /// The relative path to the OAuth callback endpoint.
    /// </summary>
    private const string OAuthCallbackPath = "/oauth/callback";

    /// <summary>
    /// The well-known path for client metadata.
    /// </summary>
    private const string ClientMetadataPath = "/.well-known/client-metadata.json";

    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<ATProtoOAuthService> _logger;
    private readonly string _clientId;
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ATProtoOAuthService"/> class.
    /// </summary>
    /// <param name="dbContextFactory">Factory for creating database contexts.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="clientId">The OAuth client_id URL (must be the client-metadata.json URL).</param>
    /// <param name="httpClient">HTTP client for token endpoint requests.</param>
    public ATProtoOAuthService(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<ATProtoOAuthService> logger,
        string clientId,
        HttpClient httpClient
    ) {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException( nameof( dbContextFactory ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
        _clientId = clientId ?? throw new ArgumentNullException( nameof( clientId ) );
        _httpClient = httpClient ?? throw new ArgumentNullException( nameof( httpClient ) );

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

        // Step 4: Generate PKCE code verifier and challenge
        string codeVerifier = GenerateCodeVerifier( );
        string codeChallenge = GenerateCodeChallenge( codeVerifier );

        // Step 5: Generate state parameter
        string state = GenerateState( );

        // Step 6: Generate DPoP key pair
        string dpoPKeyJwk = GenerateDPoPKey( );

        // Step 7: Store the OAuth state for later verification
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

        // Step 8: Build the authorization URL
        // Note: This is a simplified version. The full ATProto OAuth requires PAR (Pushed Authorization Request)
        // For now, we'll construct a basic authorization URL - this will need enhancement
        // when the idunno library's OAuth support is fully available
        Uri authorizationUrl = BuildAuthorizationUrl(
            authorizationServer,
            _clientId,
            redirectUri,
            state,
            codeChallenge,
            handle
        );

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

        // Step 4: Verify the returned DID matches the expected DID
        if (!string.Equals( result.Did, oauthState.Did, StringComparison.OrdinalIgnoreCase )) {
            _logger.LogWarning(
                "DID mismatch in token response. Expected: {Expected}, Got: {Actual}",
                oauthState.Did,
                result.Did
            );
            throw new InvalidOperationException( "Account DID does not match. Possible security issue." );
        }

        // Step 5: Clean up the OAuth state (single-use)
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
    /// Base64 URL encodes the given bytes.
    /// </summary>
    private static string Base64UrlEncode( byte[] bytes ) {
        return Convert.ToBase64String( bytes )
            .TrimEnd( '=' )
            .Replace( '+', '-' )
            .Replace( '/', '_' );
    }

    /// <summary>
    /// Builds the OAuth authorization URL.
    /// Note: ATProto requires PAR, so this is a simplified placeholder.
    /// </summary>
    private static Uri BuildAuthorizationUrl(
        Uri authorizationServer,
        string clientId,
        Uri redirectUri,
        string state,
        string codeChallenge,
        string loginHint
    ) {
        // Note: This is a placeholder. Full ATProto OAuth requires:
        // 1. PAR request to pushed_authorization_request_endpoint
        // 2. Get request_uri from PAR response
        // 3. Redirect to authorization_endpoint with request_uri
        // For now, we build a direct authorization URL which may work with some servers

        UriBuilder builder = new( authorizationServer ) {
            Path = "/oauth/authorize"
        };

        string scope = string.Join( " ", s_requiredScopes );

        System.Collections.Specialized.NameValueCollection query = System.Web.HttpUtility.ParseQueryString( string.Empty );
        query["client_id"] = clientId;
        query["response_type"] = "code";
        query["redirect_uri"] = redirectUri.ToString( );
        query["state"] = state;
        query["scope"] = scope;
        query["code_challenge"] = codeChallenge;
        query["code_challenge_method"] = "S256";
        query["login_hint"] = loginHint;

        builder.Query = query.ToString( );

        return builder.Uri;
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

        // Step 1: Build the token endpoint URL
        Uri tokenEndpoint = new( new Uri( oauthState.AuthorizationServerUri! ), "/oauth/token" );

        // Step 2: Create DPoP proof JWT for the token request
        string dpopProof = CreateDPoPProof(
            oauthState.DPoPKeyJwk!,
            "POST",
            tokenEndpoint.ToString( ),
            null
        );

        // Step 3: Build the token request
        Dictionary<string, string> formData = new( ) {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = _clientId,
            ["redirect_uri"] = $"{_baseUrl}{OAuthCallbackPath}",
            ["code_verifier"] = oauthState.CodeVerifier!
        };

        using HttpRequestMessage request = new( HttpMethod.Post, tokenEndpoint ) {
            Content = new FormUrlEncodedContent( formData )
        };
        request.Headers.Add( "DPoP", dpopProof );

        // Step 4: Send the token request
        using HttpResponseMessage response = await _httpClient.SendAsync( request, cancellationToken );

        if (!response.IsSuccessStatusCode) {
            string errorContent = await response.Content.ReadAsStringAsync( cancellationToken );
            _logger.LogError(
                "Token exchange failed with status {StatusCode}. Response: {Response}",
                response.StatusCode,
                errorContent
            );
            throw new InvalidOperationException( $"Token exchange failed: {response.StatusCode}" );
        }

        // Step 5: Parse the token response
        string responseContent = await response.Content.ReadAsStringAsync( cancellationToken );
        using JsonDocument doc = JsonDocument.Parse( responseContent );
        JsonElement root = doc.RootElement;

        string accessToken = root.GetProperty( "access_token" ).GetString( )
            ?? throw new InvalidOperationException( "access_token missing from token response" );
        string refreshToken = root.GetProperty( "refresh_token" ).GetString( )
            ?? throw new InvalidOperationException( "refresh_token missing from token response" );
        int expiresIn = root.GetProperty( "expires_in" ).GetInt32( );
        string? scope = root.TryGetProperty( "scope", out JsonElement scopeElem ) ? scopeElem.GetString( ) : null;
        string? sub = root.TryGetProperty( "sub", out JsonElement subElem ) ? subElem.GetString( ) : null;

        // Step 6: Validate the sub (DID) if present
        if (!string.IsNullOrWhiteSpace( sub ) && !string.Equals( sub, oauthState.Did, StringComparison.OrdinalIgnoreCase )) {
            _logger.LogWarning(
                "DID mismatch in token response. Expected: {Expected}, Got: {Actual}",
                oauthState.Did,
                sub
            );
            throw new InvalidOperationException( "Token response DID does not match expected DID" );
        }

        _logger.LogInformation(
            "Successfully exchanged authorization code for tokens. DID: {Did}",
            oauthState.Did
        );

        // Step 7: Return the OAuth result
        return new ATProtoOAuthResult {
            Did = sub ?? oauthState.Did!,
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
    private Task<ATProtoOAuthResult?> RefreshTokensFromServerAsync(
        Uri authorizationServer,
        string refreshToken,
        string dpoPKeyJwk,
        string did,
        CancellationToken cancellationToken
    ) {
        // TODO: Implement actual token refresh with DPoP
        // This requires:
        // 1. Constructing a DPoP proof JWT signed with the stored key
        // 2. Calling the token endpoint with grant_type=refresh_token
        // 3. Parsing the response for new access_token, refresh_token, expires_in

        throw new NotImplementedException(
            "ATProto OAuth token refresh requires full DPoP implementation. " +
            "This will be completed when the idunno.AtProto library's OAuth support is fully available " +
            "or when direct HTTP token endpoint integration is implemented."
        );
    }

    /// <summary>
    /// Creates a DPoP proof JWT for the given HTTP method and URL.
    /// </summary>
    /// <param name="dpoPKeyJwk">The DPoP private key in JWK format.</param>
    /// <param name="httpMethod">The HTTP method (e.g., "POST", "GET").</param>
    /// <param name="url">The full URL of the request.</param>
    /// <param name="accessToken">Optional access token to bind in the proof.</param>
    /// <returns>The DPoP proof JWT.</returns>
    private static string CreateDPoPProof( string dpoPKeyJwk, string httpMethod, string url, string? accessToken ) {
        // Parse the JWK to get the EC key parameters
        using JsonDocument jwkDoc = JsonDocument.Parse( dpoPKeyJwk );
        JsonElement jwk = jwkDoc.RootElement;

        string x = jwk.GetProperty( "x" ).GetString( )!;
        string y = jwk.GetProperty( "y" ).GetString( )!;
        string d = jwk.GetProperty( "d" ).GetString( )!;
        string kid = jwk.GetProperty( "kid" ).GetString( )!;

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
        SecurityTokenDescriptor descriptor = new( ) {
            Claims = new Dictionary<string, object> {
                ["htm"] = httpMethod,
                ["htu"] = url,
                ["jti"] = Guid.NewGuid( ).ToString( "N" ),
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds( )
            },
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

        SecurityToken token = handler.CreateToken( descriptor );
        return handler.WriteToken( token );
    }

    /// <summary>
    /// Base64 URL decodes a string.
    /// </summary>
    private static byte[] Base64UrlDecode( string base64Url ) {
        string padded = base64Url.PadRight( base64Url.Length + (4 - base64Url.Length % 4) % 4, '=' );
        string base64 = padded.Replace( '-', '+' ).Replace( '_', '/' );
        return Convert.FromBase64String( base64 );
    }

    #endregion
}
