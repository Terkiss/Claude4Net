using System;
using System.Collections.Generic;
using System.Linq;
using Claude4Net.Commands;

namespace Claude4Net.Cli.Ui.Input
{
    /// <summary>
    /// Provides command suggestions based on the current input.
    /// </summary>
    public sealed class CommandSuggester
    {
        /// <summary>
        /// Returns a full command suggestion if a match is found.
        /// </summary>
        public string? GetSuggestion(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            // Only suggest for commands starting with '/' or '!'
            if (!input.StartsWith("/") && !input.StartsWith("!")) return null;

            char leader = input[0];
            string prefix = input.Substring(1).ToLowerInvariant();

            // Find matching commands from CommandRegistry
            var commands = CommandRegistry.GetCommands();
            var match = commands
                .OrderBy(c => c.Name)
                .FirstOrDefault(c => c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            if (match == null) return null;

            // Return full command string
            return leader + match.Name;
        }
    }
}
