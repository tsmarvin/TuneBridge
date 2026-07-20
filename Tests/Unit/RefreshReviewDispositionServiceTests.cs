using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Web.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>Tests revision-safe, recoverable refresh-review disposition.</summary>
[TestClass]
public class RefreshReviewDispositionServiceTests {
    private const string SourceUri = "at://did:plc:test/link.bridgebeats.lookup/track:USRC12345678";
    private const string SourceCid = "bafyreihash";

    /// <summary>A matching CID is deleted before review state is cleared.</summary>
    [TestMethod]
    public async Task Delete_MatchingRevision_DeletesThenClearsReview( ) {
        Mock<IRefreshReviewStore> reviewStore = new( );
        Mock<IATProtoStorageService> storage = new( );
        int sequence = 0;
        int pdsOrder = 0;
        int reviewOrder = 0;
        _ = reviewStore.Setup( store => store.GetUnresolvedAsync( It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( [CreateEntry( )] );
        _ = storage.Setup( service => service.DeleteMediaLinkResultAsync(
                SourceUri, SourceCid, It.IsAny<CancellationToken>( ) ) )
            .Callback( ( ) => pdsOrder = Interlocked.Increment( ref sequence ) )
            .ReturnsAsync( MediaLinkDeleteOutcome.Deleted );
        _ = reviewStore.Setup( store => store.DeleteUnresolvedAsync(
                SourceUri, "refresh-saga", SourceCid, It.IsAny<CancellationToken>( ) ) )
            .Callback( ( ) => reviewOrder = Interlocked.Increment( ref sequence ) )
            .ReturnsAsync( true );

        RefreshReviewDispositionOutcome outcome = await CreateService( reviewStore, storage ).DeleteAsync(
            SourceUri, "refresh-saga", SourceCid, "admin", CancellationToken.None );

        Assert.AreEqual( RefreshReviewDispositionOutcome.Deleted, outcome );
        Assert.IsLessThan( reviewOrder, pdsOrder );
    }

    /// <summary>A changed revision is preserved while its obsolete review entry is cleared.</summary>
    [TestMethod]
    public async Task Delete_RevisionChanged_PreservesCurrentRecordAndClearsReview( ) {
        Mock<IRefreshReviewStore> reviewStore = new( );
        Mock<IATProtoStorageService> storage = new( );
        _ = reviewStore.Setup( store => store.GetUnresolvedAsync( It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( [CreateEntry( )] );
        _ = storage.Setup( service => service.DeleteMediaLinkResultAsync(
                SourceUri, SourceCid, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( MediaLinkDeleteOutcome.RevisionConflict );
        _ = reviewStore.Setup( store => store.DeleteUnresolvedAsync(
                SourceUri, "refresh-saga", SourceCid, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( true );

        RefreshReviewDispositionOutcome outcome = await CreateService( reviewStore, storage ).DeleteAsync(
            SourceUri, "refresh-saga", SourceCid, "admin", CancellationToken.None );

        Assert.AreEqual( RefreshReviewDispositionOutcome.RevisionChanged, outcome );
        reviewStore.Verify( store => store.DeleteUnresolvedAsync(
            SourceUri, "refresh-saga", SourceCid, It.IsAny<CancellationToken>( ) ), Times.Once );
    }

    /// <summary>An already-absent PDS record still permits recovery of leftover Redis review state.</summary>
    [TestMethod]
    public async Task Delete_RecordAlreadyAbsent_ClearsReview( ) {
        Mock<IRefreshReviewStore> reviewStore = new( );
        Mock<IATProtoStorageService> storage = new( );
        _ = reviewStore.Setup( store => store.GetUnresolvedAsync( It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( [CreateEntry( )] );
        _ = storage.Setup( service => service.DeleteMediaLinkResultAsync(
                SourceUri, SourceCid, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( MediaLinkDeleteOutcome.NotFound );
        _ = reviewStore.Setup( store => store.DeleteUnresolvedAsync(
                SourceUri, "refresh-saga", SourceCid, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( true );

        RefreshReviewDispositionOutcome outcome = await CreateService( reviewStore, storage ).DeleteAsync(
            SourceUri, "refresh-saga", SourceCid, "admin", CancellationToken.None );

        Assert.AreEqual( RefreshReviewDispositionOutcome.AlreadyAbsent, outcome );
        reviewStore.Verify( store => store.DeleteUnresolvedAsync(
            SourceUri, "refresh-saga", SourceCid, It.IsAny<CancellationToken>( ) ), Times.Once );
    }

    /// <summary>Legacy review entries without a CID cannot authorize an unsafe unconditional delete.</summary>
    [TestMethod]
    public async Task Delete_MissingRevision_RefusesDeletion( ) {
        Mock<IRefreshReviewStore> reviewStore = new( );
        Mock<IATProtoStorageService> storage = new( );
        _ = reviewStore.Setup( store => store.GetUnresolvedAsync( It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( [CreateEntry( ) with { SourceRecordCid = null }] );

        RefreshReviewDispositionOutcome outcome = await CreateService( reviewStore, storage ).DeleteAsync(
            SourceUri, "refresh-saga", SourceCid, "admin", CancellationToken.None );

        Assert.AreEqual( RefreshReviewDispositionOutcome.RevisionUnavailable, outcome );
        storage.Verify( service => service.DeleteMediaLinkResultAsync(
            It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ), Times.Never );
    }

    /// <summary>A stale form cannot dispose a replacement review entry for the same source URI.</summary>
    [TestMethod]
    public async Task Delete_ReviewEntryChanged_PreservesReplacement( ) {
        Mock<IRefreshReviewStore> reviewStore = new( );
        Mock<IATProtoStorageService> storage = new( );
        _ = reviewStore.Setup( store => store.GetUnresolvedAsync( It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( [CreateEntry( ) with {
                SagaId = "replacement-saga",
                SourceRecordCid = "bafyreinewer"
            }] );

        RefreshReviewDispositionOutcome outcome = await CreateService( reviewStore, storage ).DeleteAsync(
            SourceUri, "refresh-saga", SourceCid, "admin", CancellationToken.None );

        Assert.AreEqual( RefreshReviewDispositionOutcome.ReviewEntryChanged, outcome );
        storage.Verify( service => service.DeleteMediaLinkResultAsync(
            It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ), Times.Never );
        reviewStore.Verify( store => store.DeleteUnresolvedAsync(
            It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<string>( ),
            It.IsAny<CancellationToken>( ) ), Times.Never );
    }

    private static RefreshReviewDispositionService CreateService(
        Mock<IRefreshReviewStore> reviewStore,
        Mock<IATProtoStorageService> storage
    ) => new( reviewStore.Object, storage.Object, Mock.Of<ILogger<RefreshReviewDispositionService>>( ) );

    private static RefreshReviewEntry CreateEntry( ) => new( ) {
        SourceRecordUri = SourceUri,
        SourceRecordCid = SourceCid,
        SagaId = "refresh-saga",
        LookupType = LookupRequestType.IsrcLookup,
        LookupValue = "USRC12345678",
        FailedAt = DateTimeOffset.UtcNow
    };
}
