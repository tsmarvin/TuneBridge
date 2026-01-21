using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Services.Queue;
using Microsoft.Extensions.Logging;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SagaResultCombiner"/> to verify proper combination
/// of results from multiple providers into a unified <see cref="MediaLinkResult"/>.
/// </summary>
[TestClass]
public class SagaResultCombinerTests {
    private Mock<ILogger<SagaResultCombiner>> _loggerMock = null!;
    private SagaResultCombiner _combiner = null!;

    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [TestInitialize]
    public void Initialize( ) {
        _loggerMock = new Mock<ILogger<SagaResultCombiner>>( );
        _combiner = new SagaResultCombiner( _loggerMock.Object );
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidLogger_ShouldCreateInstance( ) {
        // Act
        SagaResultCombiner combiner = new( _loggerMock.Object );

        // Assert
        Assert.IsNotNull( combiner );
    }

    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) => new SagaResultCombiner( null! ) );
    }

    #endregion

    #region CombineResults(LookupSagaState) Tests

    [TestMethod]
    public void CombineResults_WithNullSaga_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) => _combiner.CombineResults( null! ) );
    }

    [TestMethod]
    public void CombineResults_WithNoProviderStates_ShouldReturnNull( ) {
        // Arrange
        LookupSagaState saga = CreateSaga( );

        // Act
        MediaLinkResult? result = _combiner.CombineResults( saga );

        // Assert
        Assert.IsNull( result );
    }

    [TestMethod]
    public void CombineResults_WithAllFailedProviders_ShouldReturnNull( ) {
        // Arrange
        LookupSagaState saga = CreateSaga( ) with {
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = CreateProviderState( SupportedProviders.Spotify, isSuccess: false ),
                [SupportedProviders.AppleMusic] = CreateProviderState( SupportedProviders.AppleMusic, isSuccess: false )
            }
        };

        // Act
        MediaLinkResult? result = _combiner.CombineResults( saga );

        // Assert
        Assert.IsNull( result );
    }

    [TestMethod]
    public void CombineResults_WithSingleSuccessfulProvider_ShouldReturnResult( ) {
        // Arrange
        MusicLookupResult spotifyResult = CreateLookupResult( "spotify-track-1", "Test Artist", "Test Song" );
        LookupSagaState saga = CreateSaga( ) with {
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = CreateProviderState( SupportedProviders.Spotify, spotifyResult )
            }
        };

        // Act
        MediaLinkResult? result = _combiner.CombineResults( saga );

        // Assert
        Assert.IsNotNull( result );
        Assert.HasCount( 1, result.Results );
        Assert.IsTrue( result.Results.ContainsKey( SupportedProviders.Spotify ) );
        Assert.AreEqual( "Test Artist", result.Results[SupportedProviders.Spotify].Artist );
        Assert.AreEqual( "Test Song", result.Results[SupportedProviders.Spotify].Title );
    }

    [TestMethod]
    public void CombineResults_WithMultipleSuccessfulProviders_ShouldReturnAllResults( ) {
        // Arrange
        MusicLookupResult spotifyResult = CreateLookupResult( "spotify-track-1", "Artist", "Song" );
        MusicLookupResult appleResult = CreateLookupResult( "apple-track-1", "Artist", "Song" );
        MusicLookupResult tidalResult = CreateLookupResult( "tidal-track-1", "Artist", "Song" );

        LookupSagaState saga = CreateSaga( ) with {
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = CreateProviderState( SupportedProviders.Spotify, spotifyResult ),
                [SupportedProviders.AppleMusic] = CreateProviderState( SupportedProviders.AppleMusic, appleResult ),
                [SupportedProviders.Tidal] = CreateProviderState( SupportedProviders.Tidal, tidalResult )
            }
        };

        // Act
        MediaLinkResult? result = _combiner.CombineResults( saga );

        // Assert
        Assert.IsNotNull( result );
        Assert.HasCount( 3, result.Results );
        Assert.IsTrue( result.Results.ContainsKey( SupportedProviders.Spotify ) );
        Assert.IsTrue( result.Results.ContainsKey( SupportedProviders.AppleMusic ) );
        Assert.IsTrue( result.Results.ContainsKey( SupportedProviders.Tidal ) );
    }

    [TestMethod]
    public void CombineResults_FirstProviderResult_ShouldBeMarkedAsPrimary( ) {
        // Arrange
        MusicLookupResult spotifyResult = CreateLookupResult( "spotify-track-1", "Artist", "Song" );
        MusicLookupResult appleResult = CreateLookupResult( "apple-track-1", "Artist", "Song" );

        LookupSagaState saga = CreateSaga( ) with {
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = CreateProviderState( SupportedProviders.Spotify, spotifyResult ),
                [SupportedProviders.AppleMusic] = CreateProviderState( SupportedProviders.AppleMusic, appleResult )
            }
        };

        // Act
        MediaLinkResult? result = _combiner.CombineResults( saga );

        // Assert
        Assert.IsNotNull( result );
        // First result should be primary, others should not
        int primaryCount = result.Results.Values.Count( r => r.IsPrimary );
        Assert.AreEqual( 1, primaryCount );
    }

    [TestMethod]
    public void CombineResults_WithMixedSuccessAndFailure_ShouldReturnOnlySuccessfulResults( ) {
        // Arrange
        MusicLookupResult spotifyResult = CreateLookupResult( "spotify-track-1", "Artist", "Song" );

        LookupSagaState saga = CreateSaga( ) with {
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = CreateProviderState( SupportedProviders.Spotify, spotifyResult ),
                [SupportedProviders.AppleMusic] = CreateProviderState( SupportedProviders.AppleMusic, isSuccess: false ),
                [SupportedProviders.Tidal] = CreateProviderState( SupportedProviders.Tidal, isSuccess: false )
            }
        };

        // Act
        MediaLinkResult? result = _combiner.CombineResults( saga );

        // Assert
        Assert.IsNotNull( result );
        Assert.HasCount( 1, result.Results );
        Assert.IsTrue( result.Results.ContainsKey( SupportedProviders.Spotify ) );
    }

    [TestMethod]
    public void CombineResults_WithIncompleteSaga_ShouldStillCombineAvailableResults( ) {
        // Arrange - Saga where not all providers have completed
        MusicLookupResult spotifyResult = CreateLookupResult( "spotify-track-1", "Artist", "Song" );

        LookupSagaState saga = CreateSaga( ) with {
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = CreateProviderState( SupportedProviders.Spotify, spotifyResult ),
                // Apple is complete but failed
                [SupportedProviders.AppleMusic] = new ProviderLookupState(
                    SupportedProviders.AppleMusic,
                    IsComplete: false, // Not complete yet
                    IsSuccess: false,
                    ResultJson: null,
                    CompletedAt: null,
                    ErrorMessage: null
                )
            }
        };

        // Act
        MediaLinkResult? result = _combiner.CombineResults( saga );

        // Assert - Should still return available results and log warning
        Assert.IsNotNull( result );
        Assert.HasCount( 1, result.Results );
    }

    [TestMethod]
    public void CombineResults_WithNullResultJson_ShouldSkipProvider( ) {
        // Arrange
        LookupSagaState saga = CreateSaga( ) with {
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = new ProviderLookupState(
                    SupportedProviders.Spotify,
                    IsComplete: true,
                    IsSuccess: true, // Success but null JSON (shouldn't happen normally)
                    ResultJson: null,
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: null
                )
            }
        };

        // Act
        MediaLinkResult? result = _combiner.CombineResults( saga );

        // Assert
        Assert.IsNull( result );
    }

    [TestMethod]
    public void CombineResults_WithEmptyResultJson_ShouldSkipProvider( ) {
        // Arrange
        LookupSagaState saga = CreateSaga( ) with {
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = new ProviderLookupState(
                    SupportedProviders.Spotify,
                    IsComplete: true,
                    IsSuccess: true,
                    ResultJson: string.Empty,
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: null
                )
            }
        };

        // Act
        MediaLinkResult? result = _combiner.CombineResults( saga );

        // Assert
        Assert.IsNull( result );
    }

    [TestMethod]
    public void CombineResults_WithInvalidJson_ShouldSkipProviderAndLogWarning( ) {
        // Arrange
        MusicLookupResult spotifyResult = CreateLookupResult( "spotify-track-1", "Artist", "Song" );

        LookupSagaState saga = CreateSaga( ) with {
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = CreateProviderState( SupportedProviders.Spotify, spotifyResult ),
                [SupportedProviders.AppleMusic] = new ProviderLookupState(
                    SupportedProviders.AppleMusic,
                    IsComplete: true,
                    IsSuccess: true,
                    ResultJson: "{ invalid json }}}",
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: null
                )
            }
        };

        // Act
        MediaLinkResult? result = _combiner.CombineResults( saga );

        // Assert - Should return Spotify result and skip Apple due to invalid JSON
        Assert.IsNotNull( result );
        Assert.HasCount( 1, result.Results );
        Assert.IsTrue( result.Results.ContainsKey( SupportedProviders.Spotify ) );
        Assert.IsFalse( result.Results.ContainsKey( SupportedProviders.AppleMusic ) );
    }

    #endregion

    #region CombineResults(IEnumerable) Tests

    [TestMethod]
    public void CombineResults_EnumerableOverload_WithNullResults_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _combiner.CombineResults( (IEnumerable<(SupportedProviders, MusicLookupResult)>)null!, "test" )
        );
    }

    [TestMethod]
    public void CombineResults_EnumerableOverload_WithEmptyResults_ShouldReturnNull( ) {
        // Arrange
        List<(SupportedProviders, MusicLookupResult)> results = [];

        // Act
        MediaLinkResult? result = _combiner.CombineResults( results, "test-lookup-value" );

        // Assert
        Assert.IsNull( result );
    }

    [TestMethod]
    public void CombineResults_EnumerableOverload_WithSingleResult_ShouldReturnResult( ) {
        // Arrange
        MusicLookupResult spotifyResult = CreateLookupResult( "spotify-id", "Artist", "Song" );
        List<(SupportedProviders, MusicLookupResult)> results = [
            (SupportedProviders.Spotify, spotifyResult)
        ];

        // Act
        MediaLinkResult? result = _combiner.CombineResults( results, "isrc:US1234567890" );

        // Assert
        Assert.IsNotNull( result );
        Assert.HasCount( 1, result.Results );
        Assert.IsTrue( result.Results.ContainsKey( SupportedProviders.Spotify ) );
    }

    [TestMethod]
    public void CombineResults_EnumerableOverload_WithMultipleResults_ShouldReturnAllResults( ) {
        // Arrange
        MusicLookupResult spotifyResult = CreateLookupResult( "spotify-id", "Artist", "Song" );
        MusicLookupResult appleResult = CreateLookupResult( "apple-id", "Artist", "Song" );
        List<(SupportedProviders, MusicLookupResult)> results = [
            (SupportedProviders.Spotify, spotifyResult),
            (SupportedProviders.AppleMusic, appleResult)
        ];

        // Act
        MediaLinkResult? result = _combiner.CombineResults( results, "isrc:US1234567890" );

        // Assert
        Assert.IsNotNull( result );
        Assert.HasCount( 2, result.Results );
    }

    [TestMethod]
    public void CombineResults_EnumerableOverload_FirstResult_ShouldBeMarkedAsPrimary( ) {
        // Arrange
        MusicLookupResult spotifyResult = CreateLookupResult( "spotify-id", "Artist", "Song" );
        MusicLookupResult appleResult = CreateLookupResult( "apple-id", "Artist", "Song" );
        List<(SupportedProviders, MusicLookupResult)> results = [
            (SupportedProviders.Spotify, spotifyResult),
            (SupportedProviders.AppleMusic, appleResult)
        ];

        // Act
        MediaLinkResult? result = _combiner.CombineResults( results, "isrc:US1234567890" );

        // Assert
        Assert.IsNotNull( result );
        int primaryCount = result.Results.Values.Count( r => r.IsPrimary );
        Assert.AreEqual( 1, primaryCount );
    }

    #endregion

    #region Helper Methods

    private static LookupSagaState CreateSaga( string? sagaId = null ) => new( ) {
        SagaId = sagaId ?? "test-saga-123",
        LookupKey = "isrc:US1234567890",
        LookupType = LookupRequestType.IsrcLookup,
        LookupValue = "US1234567890",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static MusicLookupResult CreateLookupResult( string externalId, string artist, string title ) => new( ) {
        ExternalId = externalId,
        Artist = artist,
        Title = title,
        URL = $"https://example.com/{externalId}",
        ArtUrl = "https://example.com/art.jpg",
        IsAlbum = false,
        MarketRegion = "us"
    };

    private static ProviderLookupState CreateProviderState( SupportedProviders provider, MusicLookupResult result ) =>
        new(
            provider,
            IsComplete: true,
            IsSuccess: true,
            ResultJson: JsonSerializer.Serialize( result, s_jsonOptions ),
            CompletedAt: DateTimeOffset.UtcNow,
            ErrorMessage: null
        );

    private static ProviderLookupState CreateProviderState( SupportedProviders provider, bool isSuccess ) =>
        new(
            provider,
            IsComplete: true,
            IsSuccess: isSuccess,
            ResultJson: null,
            CompletedAt: DateTimeOffset.UtcNow,
            ErrorMessage: isSuccess ? null : "Lookup failed"
        );

    #endregion
}
