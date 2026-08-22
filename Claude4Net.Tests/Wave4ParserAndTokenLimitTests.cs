using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Claude4Net.Runtime.ApiServer;
using Claude4Net.Runtime.ApiServer.Streaming;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests;

public sealed class Wave4ParserAndTokenLimitTests
{
    private const int ParserLimit = 1_048_576;

    [Fact]
    public void ToolParser_UnclosedInvokeHeader_ThrowsDedicatedLimitException()
    {
        var parser = new IncrementalToolCallParser();

        Assert.Throws<IncrementalParserLimitExceededException>(() =>
            parser.ProcessChunk("<invoke name=\"" + new string('x', ParserLimit)).ToList());

        Assert.False(parser.HasToolCalls);
    }

    [Fact]
    public void ToolParser_UnclosedParameterHeader_ThrowsDedicatedLimitException()
    {
        var parser = new IncrementalToolCallParser();
        parser.ProcessChunk("<invoke name=\"tool\">").ToList();

        Assert.Throws<IncrementalParserLimitExceededException>(() =>
            parser.ProcessChunk("<parameter name=\"" + new string('x', ParserLimit)).ToList());

        Assert.False(parser.HasToolCalls);
    }

    [Fact]
    public void ToolParser_OversizedInvokeBody_ThrowsDedicatedLimitException()
    {
        var parser = new IncrementalToolCallParser();
        parser.ProcessChunk("<invoke name=\"tool\">").ToList();

        Assert.Throws<IncrementalParserLimitExceededException>(() =>
            parser.ProcessChunk(new string(' ', ParserLimit + 1)).ToList());

        Assert.False(parser.HasToolCalls);
    }

    [Fact]
    public void ToolParser_OversizedParameterValue_ThrowsDedicatedLimitException()
    {
        var parser = new IncrementalToolCallParser();
        parser.ProcessChunk("<invoke name=\"tool\"><parameter name=\"value\">").ToList();

        Assert.Throws<IncrementalParserLimitExceededException>(() =>
            parser.ProcessChunk(new string('x', ParserLimit + 1)).ToList());

        Assert.False(parser.HasToolCalls);
    }

    [Fact]
    public void ToolParser_OverflowThenFlush_EmitsNoExecutableToolCall()
    {
        var parser = new IncrementalToolCallParser();
        parser.ProcessChunk("<invoke name=\"tool\"><parameter name=\"value\">").ToList();
        Assert.Throws<IncrementalParserLimitExceededException>(() =>
            parser.ProcessChunk(new string('x', ParserLimit + 1)).ToList());

        List<ToolParsedEvent> flushed = parser.Flush().ToList();

        Assert.Empty(flushed.Where(parsed => parsed.Type != ToolParsedEventType.ContentDelta));
        Assert.False(parser.HasToolCalls);
    }

    [Fact]
    public void ToolParser_RepeatedFlush_IsIdempotent()
    {
        var parser = new IncrementalToolCallParser();
        parser.ProcessChunk("<invoke name=\"tool\"><parameter name=\"value\">partial").ToList();

        List<ToolParsedEvent> first = parser.Flush().ToList();
        List<ToolParsedEvent> second = parser.Flush().ToList();

        Assert.NotEmpty(first);
        Assert.Empty(second);
    }

    [Fact]
    public void ReasoningParser_UnclosedReasoning_IsBounded()
    {
        var parser = new IncrementalReasoningParser();
        parser.ProcessChunk("<think>").ToList();

        Assert.Throws<IncrementalParserLimitExceededException>(() =>
            parser.ProcessChunk(new string('x', ParserLimit + 1)).ToList());

        Assert.Empty(parser.Flush());
    }

    [Fact]
    public void ReasoningParser_ContentChunkSplitInsideSurrogatePair_ReassemblesScalar()
    {
        var parser = new IncrementalReasoningParser();

        List<ReasoningParsedChunk> first = parser.ProcessChunk("\uD83D").ToList();
        string output = string.Concat(first
            .Concat(parser.ProcessChunk("\uDE00"))
            .Select(chunk => chunk.Text));

        Assert.Empty(first);
        Assert.Equal("😀", output);
        Assert.True(char.IsSurrogatePair(output, 0));
    }

    [Fact]
    public void ReasoningParser_ReasoningChunkSplitInsideSurrogatePair_ReassemblesScalar()
    {
        var parser = new IncrementalReasoningParser();
        parser.ProcessChunk("<think>").ToList();

        List<ReasoningParsedChunk> first = parser.ProcessChunk("\uD83D").ToList();
        List<ReasoningParsedChunk> output = first
            .Concat(parser.ProcessChunk("\uDE00"))
            .Concat(parser.ProcessChunk("</think>"))
            .ToList();

        Assert.Empty(first);
        Assert.Equal("😀", string.Concat(output
            .Where(chunk => chunk.Kind == ReasoningChunkKind.Reasoning)
            .Select(chunk => chunk.Text)));
    }

    [Fact]
    public void ToolParser_ContentChunkSplitInsideSurrogatePair_ReassemblesScalar()
    {
        var parser = new IncrementalToolCallParser();

        List<ToolParsedEvent> first = parser.ProcessChunk("\uD83D").ToList();
        string output = string.Concat(first
            .Concat(parser.ProcessChunk("\uDE00"))
            .Where(parsed => parsed.Type == ToolParsedEventType.ContentDelta)
            .Select(parsed => parsed.Content));

        Assert.Empty(first);
        Assert.Equal("😀", output);
        Assert.True(char.IsSurrogatePair(output, 0));
    }

    [Fact]
    public void ToolParser_ParameterChunkSplitInsideSurrogatePair_ProducesValidJsonScalar()
    {
        var parser = new IncrementalToolCallParser();
        List<ToolParsedEvent> output = parser
            .ProcessChunk("<invoke name=\"tool\"><parameter name=\"value\">")
            .ToList();

        List<ToolParsedEvent> first = parser.ProcessChunk("\uD83D").ToList();
        output.AddRange(first);
        output.AddRange(parser.ProcessChunk("\uDE00").ToList());
        output.AddRange(parser.ProcessChunk("</parameter></invoke>").ToList());

        Assert.Empty(first);
        string arguments = string.Concat(output
            .Where(parsed => parsed.Type == ToolParsedEventType.ToolCallArgumentDelta)
            .Select(parsed => parsed.ArgumentDelta));
        using JsonDocument document = JsonDocument.Parse(arguments);
        Assert.Equal("😀", document.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public void Parser_TrailingHighSurrogate_CountsTowardPendingUtf16Limit()
    {
        var reasoningParser = new IncrementalReasoningParser();
        Assert.Empty(reasoningParser.ProcessChunk("\uD83D"));
        Assert.Throws<IncrementalParserLimitExceededException>(() =>
            reasoningParser.ProcessChunk(new string('x', ParserLimit)).ToList());

        var toolParser = new IncrementalToolCallParser();
        Assert.Empty(toolParser.ProcessChunk("\uD83D"));
        Assert.Throws<IncrementalParserLimitExceededException>(() =>
            toolParser.ProcessChunk(new string('x', ParserLimit)).ToList());
    }

    [Fact]
    public void Parser_ExactUtf16BoundaryEndingInSurrogatePair_IsAcceptedIntact()
    {
        var parser = new IncrementalReasoningParser();
        string input = new string('x', ParserLimit - 2) + "😀";

        string output = string.Concat(parser.ProcessChunk(input).Select(chunk => chunk.Text));

        Assert.Equal(ParserLimit, output.Length);
        Assert.EndsWith("😀", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyTokenLimit_EmojiBoundary_DoesNotReturnUnpairedSurrogate()
    {
        MethodInfo? method = typeof(Claude4NetApiServer).GetMethod(
            "ApplyTokenLimit",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object?[] arguments = { "A😀B", 2, new Utf16TokenCounter(), false };

        string result = Assert.IsType<string>(method!.Invoke(null, arguments));

        Assert.Equal("A", result);
        Assert.True(Assert.IsType<bool>(arguments[3]));
        Assert.DoesNotContain(result, char.IsSurrogate);
    }

    [Fact]
    public void ApplyTokenLimit_PreservesLargestPrefixWithinTokenLimit()
    {
        MethodInfo? method = typeof(Claude4NetApiServer).GetMethod(
            "ApplyTokenLimit",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object?[] arguments = { "abcd", 1, new Utf16TokenCounter(), false };

        string result = Assert.IsType<string>(method!.Invoke(null, arguments));

        Assert.Equal("a", result);
        Assert.True(Assert.IsType<bool>(arguments[3]));
    }

    private sealed class Utf16TokenCounter : ITokenCounter
    {
        public int CountTokens(string text) => text.Length;
        public int CountTokens(object message) => 0;
        public int CountTokens(IEnumerable<object> messages) => 0;
    }
}
