using System.ComponentModel.DataAnnotations;

namespace BridgeBeats.Contracts.Records {

    /// <summary>Request to process an Apple Music playlist, identified by its id.</summary>
    /// <param name="PlaylistId">
    /// The Apple Music library playlist id to process. Must match the Apple library-playlist identifier
    /// grammar: only alphanumeric characters, dots, underscores, and hyphens are permitted. The
    /// expected form is <c>p.&lt;alphanumeric&gt;</c>, e.g. <c>p.ABCdef1234</c>. Maximum 100 characters.
    /// </param>
    public record ProcessPlaylistRequest(
        [Required]
        [StringLength( 100, MinimumLength = 1 )]
        [RegularExpression(
            @"^[A-Za-z0-9._-]+$",
            ErrorMessage = "PlaylistId contains invalid characters. Only alphanumeric characters, dots, underscores, and hyphens are allowed."
        )]
        string PlaylistId
    );

}
