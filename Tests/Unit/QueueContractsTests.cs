using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for Phase 2 queue contract records and enums.
/// Tests record equality, JSON serialization round-trips, and enum value coverage.
/// </summary>
[TestClass]
public class QueueContractsTests {
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    #region QueuePriority Enum Tests

    [TestMethod]
    public void QueuePriority_HasExpectedValues( ) {
        // Assert all expected values exist with correct numeric values
        int interactive = ( int )QueuePriority.Interactive;
        int background = ( int )QueuePriority.Background;
        int bulk = ( int )QueuePriority.Bulk;

        Assert.AreEqual( 0, interactive );
        Assert.AreEqual( 1, background );
        Assert.AreEqual( 2, bulk );
    }

    [TestMethod]
    public void QueuePriority_HasExactlyThreeValues( ) {
        // Arrange
        QueuePriority[] values = Enum.GetValues<QueuePriority>( );

        // Assert
#pragma warning disable MSTEST0037 // Use 'Assert.HasCount' - not available in current MSTest version
        Assert.AreEqual( 3, values.Length );
#pragma warning restore MSTEST0037
    }

    #endregion

    #region QueuedLookupRequest Tests

    [TestMethod]
    public void QueuedLookupRequest_Equality_ReturnsTrueForIdenticalRecords( ) {
        // Arrange
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        QueuedLookupRequest request1 = new( ) {
            RequestId = "req-123",
            Provider = SupportedProviders.Spotify,
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "US1234567890",
            SagaId = "saga-456",
            CreatedAt = createdAt
        };
        QueuedLookupRequest request2 = new( ) {
            RequestId = "req-123",
            Provider = SupportedProviders.Spotify,
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "US1234567890",
            SagaId = "saga-456",
            CreatedAt = createdAt
        };

        // Assert
        Assert.AreEqual( request1, request2 );
        Assert.AreEqual( request1.GetHashCode( ), request2.GetHashCode( ) );
    }

    [TestMethod]
    public void QueuedLookupRequest_Equality_ReturnsFalseForDifferentRecords( ) {
        // Arrange
        QueuedLookupRequest request1 = new( ) {
            RequestId = "req-123",
            Provider = SupportedProviders.Spotify,
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "US1234567890",
            SagaId = "saga-456"
        };
        QueuedLookupRequest request2 = new( ) {
            RequestId = "req-789",  // Different ID
            Provider = SupportedProviders.Spotify,
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "US1234567890",
            SagaId = "saga-456"
        };

        // Assert
        Assert.AreNotEqual( request1, request2 );
    }

    [TestMethod]
    public void QueuedLookupRequest_Serialization_RoundTripsCorrectly( ) {
        // Arrange
        DateTimeOffset createdAt = DateTimeOffset.Parse( "2026-01-20T12:00:00Z" );
        QueuedLookupRequest original = new( ) {
            RequestId = "req-123",
            Provider = SupportedProviders.AppleMusic,
            LookupType = LookupRequestType.UpcLookup,
            LookupValue = "0123456789012",
            Artist = "Test Artist",
            Title = "Test Title",
            IsAlbum = true,
            SagaId = "saga-789",
            CreatedAt = createdAt,
            AttemptCount = 2,
            RateLimitedEndpoint = "/v1/catalog/us/albums"
        };

        // Act
        string json = JsonSerializer.Serialize( original, s_jsonOptions );
        QueuedLookupRequest? deserialized = JsonSerializer.Deserialize<QueuedLookupRequest>( json, s_jsonOptions );

        // Assert
        Assert.IsNotNull( deserialized );
        Assert.AreEqual( original, deserialized );
    }

    [TestMethod]
    public void QueuedLookupRequest_Serialization_OmitsNullOptionalFields( ) {
        // Arrange
        QueuedLookupRequest request = new( ) {
            RequestId = "req-123",
            Provider = SupportedProviders.Tidal,
            LookupType = LookupRequestType.SongIdLookup,
            LookupValue = "12345678",
            SagaId = "saga-456"
        };

        // Act
        string json = JsonSerializer.Serialize( request, s_jsonOptions );

        // Assert - null optional fields should be omitted
#pragma warning disable MSTEST0037 // Use 'Assert.DoesNotContain' - not available in current MSTest version
        Assert.IsFalse( json.Contains( "\"artist\"" ), "JSON should not contain 'artist' field" );
        Assert.IsFalse( json.Contains( "\"title\"" ), "JSON should not contain 'title' field" );
        Assert.IsFalse( json.Contains( "\"rateLimitedEndpoint\"" ), "JSON should not contain 'rateLimitedEndpoint' field" );
#pragma warning restore MSTEST0037
    }

    #endregion

    #region QueuedMessage<T> Tests

    [TestMethod]
    public void QueuedMessage_Equality_ReturnsTrueForIdenticalRecords( ) {
        // Arrange
        DateTimeOffset enqueuedAt = DateTimeOffset.UtcNow;
        QueuedMessage<string> message1 = new( "msg-123", "payload", enqueuedAt );
        QueuedMessage<string> message2 = new( "msg-123", "payload", enqueuedAt );

        // Assert
        Assert.AreEqual( message1, message2 );
        Assert.AreEqual( message1.GetHashCode( ), message2.GetHashCode( ) );
    }

    [TestMethod]
    public void QueuedMessage_Serialization_RoundTripsCorrectly( ) {
        // Arrange
        DateTimeOffset enqueuedAt = DateTimeOffset.Parse( "2026-01-20T15:30:00Z" );
        QueuedMessage<string> original = new( "msg-456", "test-payload", enqueuedAt );

        // Act
        string json = JsonSerializer.Serialize( original, s_jsonOptions );
        QueuedMessage<string>? deserialized = JsonSerializer.Deserialize<QueuedMessage<string>>( json, s_jsonOptions );

        // Assert
        Assert.IsNotNull( deserialized );
        Assert.AreEqual( original, deserialized );
    }

    #endregion

    #region QueueDepth Tests

    [TestMethod]
    public void QueueDepth_Equality_ReturnsTrueForIdenticalRecords( ) {
        // Arrange
        QueueDepth depth1 = new( 10, 5, 2, 17 );
        QueueDepth depth2 = new( 10, 5, 2, 17 );

        // Assert
        Assert.AreEqual( depth1, depth2 );
        Assert.AreEqual( depth1.GetHashCode( ), depth2.GetHashCode( ) );
    }

    [TestMethod]
    public void QueueDepth_Serialization_RoundTripsCorrectly( ) {
        // Arrange
        QueueDepth original = new( 100, 50, 25, 175 );

        // Act
        string json = JsonSerializer.Serialize( original, s_jsonOptions );
        QueueDepth? deserialized = JsonSerializer.Deserialize<QueueDepth>( json, s_jsonOptions );

        // Assert
        Assert.IsNotNull( deserialized );
        Assert.AreEqual( original, deserialized );
    }

    #endregion

    #region DeduplicationResult Tests

    [TestMethod]
    public void DeduplicationResult_Equality_ReturnsTrueForIdenticalRecords( ) {
        // Arrange
        DeduplicationResult result1 = new( true, false, "isrc:US1234567890" );
        DeduplicationResult result2 = new( true, false, "isrc:US1234567890" );

        // Assert
        Assert.AreEqual( result1, result2 );
        Assert.AreEqual( result1.GetHashCode( ), result2.GetHashCode( ) );
    }

    [TestMethod]
    public void DeduplicationResult_Serialization_RoundTripsCorrectly( ) {
        // Arrange
        DeduplicationResult original = new( false, true, "url:spotify:track:abc123" );

        // Act
        string json = JsonSerializer.Serialize( original, s_jsonOptions );
        DeduplicationResult? deserialized = JsonSerializer.Deserialize<DeduplicationResult>( json, s_jsonOptions );

        // Assert
        Assert.IsNotNull( deserialized );
        Assert.AreEqual( original, deserialized );
    }

    #endregion

    #region RateLimitState Tests

    [TestMethod]
    public void RateLimitState_Equality_ReturnsTrueForIdenticalRecords( ) {
        // Arrange
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMinutes( 5 );
        TimeSpan remaining = TimeSpan.FromMinutes( 5 );
        RateLimitState state1 = new( true, retryAfter, remaining );
        RateLimitState state2 = new( true, retryAfter, remaining );

        // Assert
        Assert.AreEqual( state1, state2 );
        Assert.AreEqual( state1.GetHashCode( ), state2.GetHashCode( ) );
    }

    [TestMethod]
    public void RateLimitState_Serialization_RoundTripsCorrectly( ) {
        // Arrange
        RateLimitState original = new( true, DateTimeOffset.Parse( "2026-01-20T16:00:00Z" ), TimeSpan.FromMinutes( 3 ) );

        // Act
        string json = JsonSerializer.Serialize( original, s_jsonOptions );
        RateLimitState? deserialized = JsonSerializer.Deserialize<RateLimitState>( json, s_jsonOptions );

        // Assert
        Assert.IsNotNull( deserialized );
        Assert.AreEqual( original, deserialized );
    }

    [TestMethod]
    public void RateLimitState_Serialization_OmitsNullOptionalFields( ) {
        // Arrange
        RateLimitState notLimited = new( false, null, null );

        // Act
        string json = JsonSerializer.Serialize( notLimited, s_jsonOptions );

        // Assert
#pragma warning disable MSTEST0037 // Use 'Assert.DoesNotContain' - not available in current MSTest version
        Assert.IsFalse( json.Contains( "\"retryAfter\"" ), "JSON should not contain 'retryAfter' field" );
        Assert.IsFalse( json.Contains( "\"timeRemaining\"" ), "JSON should not contain 'timeRemaining' field" );
#pragma warning restore MSTEST0037
    }

    #endregion

    #region RateLimitedEndpoint Tests

    [TestMethod]
    public void RateLimitedEndpoint_Equality_ReturnsTrueForIdenticalRecords( ) {
        // Arrange
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMinutes( 10 );
        RateLimitedEndpoint endpoint1 = new( "/v1/search", retryAfter );
        RateLimitedEndpoint endpoint2 = new( "/v1/search", retryAfter );

        // Assert
        Assert.AreEqual( endpoint1, endpoint2 );
        Assert.AreEqual( endpoint1.GetHashCode( ), endpoint2.GetHashCode( ) );
    }

    [TestMethod]
    public void RateLimitedEndpoint_Serialization_RoundTripsCorrectly( ) {
        // Arrange
        RateLimitedEndpoint original = new( "/v1/catalog/us/songs", DateTimeOffset.Parse( "2026-01-20T17:00:00Z" ) );

        // Act
        string json = JsonSerializer.Serialize( original, s_jsonOptions );
        RateLimitedEndpoint? deserialized = JsonSerializer.Deserialize<RateLimitedEndpoint>( json, s_jsonOptions );

        // Assert
        Assert.IsNotNull( deserialized );
        Assert.AreEqual( original, deserialized );
    }

    #endregion

    #region LookupSagaState Tests

    [TestMethod]
    public void LookupSagaState_Equality_ComparesValueProperties( ) {
        // Arrange - Note: LookupSagaState contains a Dictionary which uses reference equality,
        // so two instances with the same values in different Dictionary instances are not equal.
        // This test verifies the value properties are correctly compared.
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        LookupSagaState saga1 = new( ) {
            SagaId = "saga-123",
            LookupKey = "isrc:US1234567890",
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "US1234567890",
            CreatedAt = createdAt,
            PartialResultUri = "at://test",
            FinalResultUri = "at://final"
        };
        LookupSagaState saga2 = saga1 with { }; // Create a copy using with expression

        // Assert - with expression creates a shallow copy, so ProviderStates reference is shared
        Assert.AreEqual( saga1, saga2 );
        Assert.AreEqual( saga1.GetHashCode( ), saga2.GetHashCode( ) );

        // Verify value properties
        Assert.AreEqual( saga1.SagaId, saga2.SagaId );
        Assert.AreEqual( saga1.LookupKey, saga2.LookupKey );
        Assert.AreEqual( saga1.LookupType, saga2.LookupType );
        Assert.AreEqual( saga1.LookupValue, saga2.LookupValue );
    }

    [TestMethod]
    public void LookupSagaState_Serialization_RoundTripsCorrectly( ) {
        // Arrange
        DateTimeOffset createdAt = DateTimeOffset.Parse( "2026-01-20T12:00:00Z" );
        LookupSagaState original = new( ) {
            SagaId = "saga-456",
            LookupKey = "upc:0123456789012",
            LookupType = LookupRequestType.UpcLookup,
            LookupValue = "0123456789012",
            CreatedAt = createdAt,
            PartialResultUri = "at://did:plc:abc/app.bridgebeats.lookup/xyz",
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = new( SupportedProviders.Spotify, true, true, "{}", DateTimeOffset.UtcNow, null ),
                [SupportedProviders.AppleMusic] = new( SupportedProviders.AppleMusic, false, false, null, null, null )
            }
        };

        // Act
        string json = JsonSerializer.Serialize( original, s_jsonOptions );
        LookupSagaState? deserialized = JsonSerializer.Deserialize<LookupSagaState>( json, s_jsonOptions );

        // Assert
        Assert.IsNotNull( deserialized );
        Assert.AreEqual( original.SagaId, deserialized.SagaId );
        Assert.AreEqual( original.LookupKey, deserialized.LookupKey );
        Assert.AreEqual( original.LookupType, deserialized.LookupType );
        Assert.AreEqual( original.LookupValue, deserialized.LookupValue );
        Assert.AreEqual( original.PartialResultUri, deserialized.PartialResultUri );
#pragma warning disable MSTEST0037 // Use 'Assert.HasCount' - not available in current MSTest version
        Assert.AreEqual( 2, deserialized.ProviderStates.Count );
#pragma warning restore MSTEST0037
    }

    [TestMethod]
    public void LookupSagaState_IsComplete_ReturnsFalseWhenEmpty( ) {
        // Arrange
        LookupSagaState saga = new( ) {
            SagaId = "saga-123",
            LookupKey = "isrc:test",
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "test"
        };

        // Assert
        Assert.IsFalse( saga.IsComplete );
    }

    [TestMethod]
    public void LookupSagaState_IsComplete_ReturnsFalseWhenAnyProviderIncomplete( ) {
        // Arrange
        LookupSagaState saga = new( ) {
            SagaId = "saga-123",
            LookupKey = "isrc:test",
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "test",
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = new( SupportedProviders.Spotify, true, true, "{}", DateTimeOffset.UtcNow, null ),
                [SupportedProviders.AppleMusic] = new( SupportedProviders.AppleMusic, false, false, null, null, null )
            }
        };

        // Assert
        Assert.IsFalse( saga.IsComplete );
    }

    [TestMethod]
    public void LookupSagaState_IsComplete_ReturnsTrueWhenAllProvidersComplete( ) {
        // Arrange
        LookupSagaState saga = new( ) {
            SagaId = "saga-123",
            LookupKey = "isrc:test",
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "test",
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = new( SupportedProviders.Spotify, true, true, "{}", DateTimeOffset.UtcNow, null ),
                [SupportedProviders.AppleMusic] = new( SupportedProviders.AppleMusic, true, true, "{}", DateTimeOffset.UtcNow, null )
            }
        };

        // Assert
        Assert.IsTrue( saga.IsComplete );
    }

    #endregion

    #region ProviderLookupState Tests

    [TestMethod]
    public void ProviderLookupState_Equality_ReturnsTrueForIdenticalRecords( ) {
        // Arrange
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        ProviderLookupState state1 = new( SupportedProviders.Tidal, true, true, "{\"id\": 123}", completedAt, null );
        ProviderLookupState state2 = new( SupportedProviders.Tidal, true, true, "{\"id\": 123}", completedAt, null );

        // Assert
        Assert.AreEqual( state1, state2 );
        Assert.AreEqual( state1.GetHashCode( ), state2.GetHashCode( ) );
    }

    [TestMethod]
    public void ProviderLookupState_Serialization_RoundTripsCorrectly( ) {
        // Arrange
        ProviderLookupState original = new(
            SupportedProviders.Spotify,
            true,
            false,
            null,
            DateTimeOffset.Parse( "2026-01-20T14:00:00Z" ),
            "Rate limited"
        );

        // Act
        string json = JsonSerializer.Serialize( original, s_jsonOptions );
        ProviderLookupState? deserialized = JsonSerializer.Deserialize<ProviderLookupState>( json, s_jsonOptions );

        // Assert
        Assert.IsNotNull( deserialized );
        Assert.AreEqual( original, deserialized );
    }

    #endregion

    #region QueueSettings Tests

    [TestMethod]
    public void QueueSettings_HasExpectedDefaults( ) {
        // Arrange
        QueueSettings settings = new( );

        // Assert
        Assert.AreEqual( TimeSpan.FromMinutes( 2 ), settings.RateLimitRetryThreshold );
        Assert.AreEqual( 60, settings.JobExpirationMinutes );
        Assert.IsNotNull( settings.Weights );
    }

    [TestMethod]
    public void QueueSettings_Serialization_RoundTripsCorrectly( ) {
        // Arrange
        QueueSettings original = new( ) {
            RateLimitRetryThreshold = TimeSpan.FromMinutes( 5 ),
            JobExpirationMinutes = 120,
            Weights = new PriorityWeights {
                Interactive = 10,
                Background = 5,
                Bulk = 1
            }
        };

        // Act
        string json = JsonSerializer.Serialize( original, s_jsonOptions );
        QueueSettings? deserialized = JsonSerializer.Deserialize<QueueSettings>( json, s_jsonOptions );

        // Assert
        Assert.IsNotNull( deserialized );
        Assert.AreEqual( original.RateLimitRetryThreshold, deserialized.RateLimitRetryThreshold );
        Assert.AreEqual( original.JobExpirationMinutes, deserialized.JobExpirationMinutes );
        Assert.AreEqual( original.Weights.Interactive, deserialized.Weights.Interactive );
        Assert.AreEqual( original.Weights.Background, deserialized.Weights.Background );
        Assert.AreEqual( original.Weights.Bulk, deserialized.Weights.Bulk );
    }

    #endregion

    #region PriorityWeights Tests

    [TestMethod]
    public void PriorityWeights_HasExpectedDefaults( ) {
        // Arrange
        PriorityWeights weights = new( );

        // Assert
        Assert.AreEqual( 5, weights.Interactive );
        Assert.AreEqual( 2, weights.Background );
        Assert.AreEqual( 1, weights.Bulk );
    }

    [TestMethod]
    public void PriorityWeights_Equality_ReturnsTrueForIdenticalRecords( ) {
        // Arrange
        PriorityWeights weights1 = new( ) { Interactive = 10, Background = 5, Bulk = 2 };
        PriorityWeights weights2 = new( ) { Interactive = 10, Background = 5, Bulk = 2 };

        // Assert
        Assert.AreEqual( weights1, weights2 );
        Assert.AreEqual( weights1.GetHashCode( ), weights2.GetHashCode( ) );
    }

    [TestMethod]
    public void PriorityWeights_Serialization_RoundTripsCorrectly( ) {
        // Arrange
        PriorityWeights original = new( ) { Interactive = 8, Background = 4, Bulk = 1 };

        // Act
        string json = JsonSerializer.Serialize( original, s_jsonOptions );
        PriorityWeights? deserialized = JsonSerializer.Deserialize<PriorityWeights>( json, s_jsonOptions );

        // Assert
        Assert.IsNotNull( deserialized );
        Assert.AreEqual( original, deserialized );
    }

    #endregion
}
