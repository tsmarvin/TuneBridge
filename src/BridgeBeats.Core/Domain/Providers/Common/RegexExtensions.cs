using System.Text.RegularExpressions;

namespace BridgeBeats.Core.Domain.Providers.Common {

    /// <summary>
    /// Extension helpers for <see cref="Regex"/> used by the provider services.
    /// </summary>
    /// <remarks>
    /// A near-identical public <c>GetGroupValues</c> also exists in the service-extensions helpers; this
    /// internal copy is the one used within the provider layer.
    /// </remarks>
    internal static class RegexExtensions {

        /// <summary>
        /// Enumerates the value of a named capture group across every match of <paramref name="regex"/>
        /// in <paramref name="input"/>.
        /// </summary>
        /// <param name="regex">The regex to match with.</param>
        /// <param name="input">The text to search.</param>
        /// <param name="groupName">The name of the capture group whose values to yield.</param>
        /// <returns>The named-group value from each match that contains the group, in match order.</returns>
        public static IEnumerable<string> GetGroupValues( this Regex regex, string input, string groupName ) {
            foreach (Match match in regex.Matches( input )) {
                if (match.Groups.TryGetValue( groupName, out Group? group )) {
                    yield return group.Value;
                }
            }
        }
    }

}
