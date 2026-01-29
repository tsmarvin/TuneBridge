using System.Text.RegularExpressions;

namespace BridgeBeats.Core.Domain.Providers.Common {

    /// <summary>
    /// Extension methods for regular expression operations.
    /// </summary>
    internal static class RegexExtensions {
        /// <summary>
        /// Extracts all values of a named group from regex matches.
        /// </summary>
        /// <param name="regex">The regex to match against.</param>
        /// <param name="input">The input string to search.</param>
        /// <param name="groupName">The name of the group to extract.</param>
        /// <returns>An enumerable of all group values found.</returns>
        public static IEnumerable<string> GetGroupValues( this Regex regex, string input, string groupName ) {
            foreach (Match match in regex.Matches( input )) {
                if (match.Groups.TryGetValue( groupName, out Group? group )) {
                    yield return group.Value;
                }
            }
        }
    }

}
