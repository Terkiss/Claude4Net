using System;

namespace Claude4Net.Runtime.ApiServer.Streaming;

internal static class IncrementalParserLimit
{
    internal const int MaxPendingUtf16CodeUnits = 1_048_576;
}

internal sealed class IncrementalParserLimitExceededException : InvalidOperationException
{
    internal IncrementalParserLimitExceededException()
        : base($"Incremental parser pending state exceeded {IncrementalParserLimit.MaxPendingUtf16CodeUnits} UTF-16 code units.")
    {
    }
}
