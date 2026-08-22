using System;
using System.Collections.Generic;
using System.Text;

namespace Claude4Net.Runtime.ApiServer.Streaming
{
    public enum ReasoningChunkKind
    {
        Content,
        Reasoning
    }

    public readonly struct ReasoningParsedChunk
    {
        public readonly ReasoningChunkKind Kind;
        public readonly string Text;

        public ReasoningParsedChunk(ReasoningChunkKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }
    }

    /// <summary>
    /// Stream-safe incremental reasoning parser.
    /// Uses a bounded sliding window algorithm to extract <think>...</think> blocks on-the-fly.
    /// Emits batched string spans (preserving UTF-16 surrogate pairs and preventing 1-char SSE packet amplification).
    /// Memory overhead is strictly O(1) (bounded by tag length).
    /// </summary>
    public class IncrementalReasoningParser
    {
        private const string OpenTag = "<think>";
        private const string CloseTag = "</think>";

        private enum State
        {
            InContent,
            InReasoning
        }

        private State _state = State.InContent;
        private readonly StringBuilder _pendingBuffer = new();

        public bool HasEverSeenReasoning { get; private set; }

        public IEnumerable<ReasoningParsedChunk> ProcessChunk(string? chunk)
        {
            if (string.IsNullOrEmpty(chunk)) yield break;

            AppendPending(chunk);

            while (_pendingBuffer.Length > 0)
            {
                string buffer = _pendingBuffer.ToString();

                if (_state == State.InContent)
                {
                    int openIdx = buffer.IndexOf(OpenTag, StringComparison.OrdinalIgnoreCase);
                    if (openIdx >= 0)
                    {
                        // Found complete <think> tag
                        if (openIdx > 0)
                        {
                            string contentBefore = buffer.Substring(0, openIdx);
                            yield return new ReasoningParsedChunk(ReasoningChunkKind.Content, contentBefore);
                        }

                        _state = State.InReasoning;
                        HasEverSeenReasoning = true;

                        int remainingStart = openIdx + OpenTag.Length;
                        _pendingBuffer.Clear();
                        if (remainingStart < buffer.Length)
                        {
                            AppendPending(buffer.Substring(remainingStart));
                        }
                    }
                    else
                    {
                        // No full <think> tag found. Check if the end matches a partial prefix of "<think>"
                        int partialLen = GetMatchingPrefixSuffixLength(buffer, OpenTag);
                        if (partialLen > 0)
                        {
                            int emitLen = buffer.Length - partialLen;
                            if (emitLen > 0)
                            {
                                string contentToEmit = buffer.Substring(0, emitLen);
                                _pendingBuffer.Clear();
                                AppendPending(buffer.Substring(emitLen));
                                yield return new ReasoningParsedChunk(ReasoningChunkKind.Content, contentToEmit);
                            }
                            break; // Keep partial prefix in buffer and await next chunk
                        }
                        else
                        {
                            int emitLen = GetSafeEmissionLength(buffer);
                            string contentToEmit = buffer.Substring(0, emitLen);
                            _pendingBuffer.Clear();
                            AppendPending(buffer.Substring(emitLen));
                            if (emitLen > 0)
                            {
                                yield return new ReasoningParsedChunk(ReasoningChunkKind.Content, contentToEmit);
                            }
                            break;
                        }
                    }
                }
                else // InReasoning
                {
                    int closeIdx = buffer.IndexOf(CloseTag, StringComparison.OrdinalIgnoreCase);
                    if (closeIdx >= 0)
                    {
                        // Found complete </think> tag
                        if (closeIdx > 0)
                        {
                            string reasoningBefore = buffer.Substring(0, closeIdx);
                            yield return new ReasoningParsedChunk(ReasoningChunkKind.Reasoning, reasoningBefore);
                        }

                        _state = State.InContent;

                        int remainingStart = closeIdx + CloseTag.Length;
                        _pendingBuffer.Clear();
                        if (remainingStart < buffer.Length)
                        {
                            AppendPending(buffer.Substring(remainingStart));
                        }
                    }
                    else
                    {
                        // No full </think> tag found. Check if the end matches a partial prefix of "</think>"
                        int partialLen = GetMatchingPrefixSuffixLength(buffer, CloseTag);
                        if (partialLen > 0)
                        {
                            int emitLen = buffer.Length - partialLen;
                            if (emitLen > 0)
                            {
                                string reasoningToEmit = buffer.Substring(0, emitLen);
                                _pendingBuffer.Clear();
                                AppendPending(buffer.Substring(emitLen));
                                yield return new ReasoningParsedChunk(ReasoningChunkKind.Reasoning, reasoningToEmit);
                            }
                            break; // Keep partial prefix in buffer and await next chunk
                        }
                        else
                        {
                            int emitLen = GetSafeEmissionLength(buffer);
                            string reasoningToEmit = buffer.Substring(0, emitLen);
                            _pendingBuffer.Clear();
                            AppendPending(buffer.Substring(emitLen));
                            if (emitLen > 0)
                            {
                                yield return new ReasoningParsedChunk(ReasoningChunkKind.Reasoning, reasoningToEmit);
                            }
                            break;
                        }
                    }
                }
            }
        }

        public IEnumerable<ReasoningParsedChunk> Flush()
        {
            if (_pendingBuffer.Length > 0)
            {
                string remaining = _pendingBuffer.ToString();
                _pendingBuffer.Clear();

                if (_state == State.InReasoning)
                {
                    yield return new ReasoningParsedChunk(ReasoningChunkKind.Reasoning, remaining);
                }
                else
                {
                    yield return new ReasoningParsedChunk(ReasoningChunkKind.Content, remaining);
                }
            }

            _state = State.InContent;
        }

        private void AppendPending(string text)
        {
            if (text.Length > IncrementalParserLimit.MaxPendingUtf16CodeUnits - _pendingBuffer.Length)
            {
                _pendingBuffer.Clear();
                _state = State.InContent;
                HasEverSeenReasoning = false;
                throw new IncrementalParserLimitExceededException();
            }

            _pendingBuffer.Append(text);
        }

        private static int GetSafeEmissionLength(string text)
        {
            return char.IsHighSurrogate(text[^1]) ? text.Length - 1 : text.Length;
        }

        private static int GetMatchingPrefixSuffixLength(string text, string targetTag)
        {
            int maxCheck = Math.Min(text.Length, targetTag.Length - 1);
            for (int len = maxCheck; len >= 1; len--)
            {
                string suffix = text.Substring(text.Length - len);
                if (targetTag.StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return len;
                }
            }
            return 0;
        }
    }
}
