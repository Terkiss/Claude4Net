using System;
using System.Collections.Generic;

namespace Claude4Net.Tests
{
    internal sealed class SdkRuntimeUnavailableException : Exception
    {
        internal SdkRuntimeUnavailableException(SdkRuntime runtime, IReadOnlyList<string> candidates)
            : base($"{runtime} runtime is unavailable. Tried: {string.Join(", ", candidates)}.")
        {
        }
    }

    internal sealed class SdkDependencyUnavailableException : Exception
    {
        internal SdkDependencyUnavailableException(string requiredModule, IReadOnlyList<string> candidates)
            : base($"Required SDK module '{requiredModule}' is unavailable. Checked: {string.Join(", ", candidates)}.")
        {
            RequiredModule = requiredModule;
        }

        internal string RequiredModule { get; }
    }

    internal sealed class SdkCapabilityTimeoutException : Exception
    {
        internal SdkCapabilityTimeoutException(string executable, string requiredModule, TimeSpan timeout)
            : base($"Capability check for module '{requiredModule}' using '{executable}' exceeded {timeout.TotalSeconds:0.###} seconds.")
        {
        }
    }

    internal sealed class SdkCapabilityProbeException : Exception
    {
        internal SdkCapabilityProbeException(
            string executable,
            string requiredModule,
            int exitCode,
            string standardOutput,
            string standardError)
            : base($"Capability check for module '{requiredModule}' using '{executable}' exited with code {exitCode}.\nSTDOUT:\n{standardOutput}\nSTDERR:\n{standardError}")
        {
        }
    }

    internal sealed class SdkProcessTimeoutException : Exception
    {
        internal SdkProcessTimeoutException(TimeSpan timeout, string standardOutput, string standardError)
            : base($"SDK process exceeded the {timeout.TotalSeconds:0.###}-second timeout and its process tree was terminated.")
        {
            StandardOutput = standardOutput;
            StandardError = standardError;
        }

        internal string StandardOutput { get; }
        internal string StandardError { get; }
    }

    internal sealed class SdkProcessCleanupTimeoutException : Exception
    {
        internal SdkProcessCleanupTimeoutException(string phase, TimeSpan bound)
            : base($"SDK process cleanup phase '{phase}' exceeded its {bound.TotalSeconds:0.###}-second bound.")
        {
            Phase = phase;
            Bound = bound;
        }

        internal string Phase { get; }
        internal TimeSpan Bound { get; }
    }

    internal sealed class SdkProcessExitException : Exception
    {
        internal SdkProcessExitException(int exitCode, string standardOutput, string standardError)
            : base($"SDK process exited with code {exitCode}.\nSTDOUT:\n{standardOutput}\nSTDERR:\n{standardError}")
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
        }

        internal int ExitCode { get; }
        internal string StandardOutput { get; }
        internal string StandardError { get; }
    }

    internal sealed class SdkProcessAssertionException : Exception
    {
        internal SdkProcessAssertionException(string successMarker, string standardOutput, string standardError)
            : base($"SDK process succeeded without expected assertion marker '{successMarker}'.\nSTDOUT:\n{standardOutput}\nSTDERR:\n{standardError}")
        {
            StandardOutput = standardOutput;
            StandardError = standardError;
        }

        internal string StandardOutput { get; }
        internal string StandardError { get; }
    }
}
