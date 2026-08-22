using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Claude4Net.Tests
{
    internal static class SdkRuntimeDiscovery
    {
        internal static string[] GetRuntimeCandidates(
            SdkRuntime runtime,
            Func<string, string?>? environmentReader = null,
            bool? isWindows = null,
            Func<string, bool>? isExecutable = null)
        {
            environmentReader ??= Environment.GetEnvironmentVariable;
            bool runningOnWindows = isWindows ?? OperatingSystem.IsWindows();
            isExecutable ??= IsExecutable;
            string overrideName = runtime == SdkRuntime.Python
                ? "CLAUDE4NET_TEST_PYTHON"
                : "CLAUDE4NET_TEST_NODE";
            string? runtimeOverride = environmentReader(overrideName);
            string[] commandNames = runtime switch
            {
                SdkRuntime.Python when runningOnWindows => ["python"],
                SdkRuntime.Python => ["python3", "python"],
                SdkRuntime.Node => ["node"],
                _ => throw new ArgumentOutOfRangeException(nameof(runtime), runtime, null)
            };
            var requestedCommands = string.IsNullOrWhiteSpace(runtimeOverride)
                ? commandNames
                : new[] { runtimeOverride }.Concat(commandNames);
            string[] pathEntries = (environmentReader("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string[] extensions = GetExtensions(environmentReader("PATHEXT"), runningOnWindows);
            var candidates = new List<string>();

            foreach (string command in requestedCommands)
            {
                foreach (string candidate in ResolveCommand(command, pathEntries, extensions, isExecutable))
                {
                    if (!candidates.Contains(candidate, runningOnWindows
                            ? StringComparer.OrdinalIgnoreCase
                            : StringComparer.Ordinal))
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            return candidates.ToArray();
        }

        private static IEnumerable<string> ResolveCommand(
            string command,
            IReadOnlyList<string> pathEntries,
            IReadOnlyList<string> extensions,
            Func<string, bool> isExecutable)
        {
            bool hasDirectory = Path.IsPathFullyQualified(command) ||
                                command.Contains(Path.DirectorySeparatorChar) ||
                                command.Contains(Path.AltDirectorySeparatorChar);
            IEnumerable<string> roots = hasDirectory ? [command] : pathEntries.Select(path => Path.Combine(path, command));
            foreach (string root in roots)
            {
                IEnumerable<string> paths = Path.HasExtension(root)
                    ? [root]
                    : extensions.Select(extension => root + extension);
                foreach (string path in paths)
                {
                    string fullPath = Path.GetFullPath(path.Trim('"'));
                    if (isExecutable(fullPath))
                    {
                        yield return fullPath;
                    }
                }
            }
        }

        private static string[] GetExtensions(string? pathExtensions, bool isWindows)
        {
            if (!isWindows)
            {
                return [string.Empty];
            }

            return (string.IsNullOrWhiteSpace(pathExtensions) ? ".EXE;.COM;.BAT;.CMD" : pathExtensions)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(extension => extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant())
                .ToArray();
        }

        private static bool IsExecutable(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                return true;
            }

            UnixFileMode mode = File.GetUnixFileMode(path);
            UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (mode & execute) != 0;
        }
    }
}
