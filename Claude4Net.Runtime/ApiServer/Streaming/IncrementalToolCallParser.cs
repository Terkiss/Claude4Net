using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Claude4Net.Runtime.ApiServer.Streaming
{
    public enum ToolParsedEventType
    {
        ContentDelta,
        ToolCallHeader,
        ToolCallArgumentDelta
    }

    public class ToolParsedEvent
    {
        public ToolParsedEventType Type { get; set; }
        public string? Content { get; set; }
        public int ToolIndex { get; set; }
        public string? ToolId { get; set; }
        public string? ToolName { get; set; }
        public string? ArgumentDelta { get; set; }
    }

    /// <summary>
    /// Stream-safe incremental tool call parser for Claude4Net.
    /// Parses <invoke name="tool_name"><parameter name="arg">val</parameter></invoke> blocks
    /// on-the-fly using a chunk-level sliding window parser.
    /// Preserves full multi-byte / surrogate pair Unicode characters (e.g. emojis, Korean, currency symbols),
    /// emits batched delta events to avoid 1-char SSE packet amplification,
    /// and guarantees zero XML markup leakage into content.
    /// </summary>
    public class IncrementalToolCallParser
    {
        private enum State
        {
            NormalContent,
            InInvokeHeader,
            InInvokeBody,
            InParamHeader,
            InParamValue
        }

        private const string InvokeOpenPrefix = "<invoke";
        private const string InvokeCloseTag = "</invoke>";
        private const string ParamOpenPrefix = "<parameter";
        private const string ParamCloseTag = "</parameter>";

        private State _state = State.NormalContent;
        private readonly StringBuilder _pendingBuffer = new();

        private int _currentToolIndex = 0;
        private string? _currentToolId;
        private string? _currentToolName;
        private string? _currentParamName;
        private bool _isFirstParamInCurrentTool = true;

        public bool HasToolCalls => _currentToolIndex > 0 || _currentToolId != null;

        public IEnumerable<ToolParsedEvent> ProcessChunk(string? chunk)
        {
            if (string.IsNullOrEmpty(chunk)) yield break;

            AppendPending(chunk);

            while (_pendingBuffer.Length > 0)
            {
                string buffer = _pendingBuffer.ToString();

                switch (_state)
                {
                    case State.NormalContent:
                    {
                        int invokeIdx = buffer.IndexOf(InvokeOpenPrefix, StringComparison.OrdinalIgnoreCase);
                        if (invokeIdx >= 0)
                        {
                            if (invokeIdx > 0)
                            {
                                string contentBefore = buffer.Substring(0, invokeIdx);
                                yield return new ToolParsedEvent
                                {
                                    Type = ToolParsedEventType.ContentDelta,
                                    Content = contentBefore
                                };
                            }

                            _state = State.InInvokeHeader;
                            _pendingBuffer.Clear();
                            AppendPending(buffer.Substring(invokeIdx));
                        }
                        else
                        {
                            int partialLen = GetMatchingPrefixSuffixLength(buffer, InvokeOpenPrefix);
                            if (partialLen > 0)
                            {
                                int emitLen = buffer.Length - partialLen;
                                if (emitLen > 0)
                                {
                                    string contentToEmit = buffer.Substring(0, emitLen);
                                    _pendingBuffer.Clear();
                                    AppendPending(buffer.Substring(emitLen));
                                    yield return new ToolParsedEvent
                                    {
                                        Type = ToolParsedEventType.ContentDelta,
                                        Content = contentToEmit
                                    };
                                }
                                goto LoopEnd;
                            }
                            else
                            {
                                int emitLen = GetSafeEmissionLength(buffer);
                                string contentToEmit = buffer.Substring(0, emitLen);
                                _pendingBuffer.Clear();
                                AppendPending(buffer.Substring(emitLen));
                                if (emitLen > 0)
                                {
                                    yield return new ToolParsedEvent
                                    {
                                        Type = ToolParsedEventType.ContentDelta,
                                        Content = contentToEmit
                                    };
                                }
                                goto LoopEnd;
                            }
                        }
                        break;
                    }

                    case State.InInvokeHeader:
                    {
                        int gtIdx = buffer.IndexOf('>');
                        if (gtIdx >= 0)
                        {
                            string headerTag = buffer.Substring(0, gtIdx + 1);
                            var match = Regex.Match(headerTag, @"<invoke\s+name=[""'](?<name>[^""']+)[""']\s*>", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                _currentToolName = match.Groups["name"].Value;
                                _currentToolId = "call_" + Guid.NewGuid().ToString("N")[..12];
                                _isFirstParamInCurrentTool = true;

                                yield return new ToolParsedEvent
                                {
                                    Type = ToolParsedEventType.ToolCallHeader,
                                    ToolIndex = _currentToolIndex,
                                    ToolId = _currentToolId,
                                    ToolName = _currentToolName
                                };

                                _state = State.InInvokeBody;
                                _pendingBuffer.Clear();
                                if (gtIdx + 1 < buffer.Length)
                                {
                                    AppendPending(buffer.Substring(gtIdx + 1));
                                }
                            }
                            else
                            {
                                // Malformed invoke tag, revert to content
                                _state = State.NormalContent;
                                _pendingBuffer.Clear();
                                if (gtIdx + 1 < buffer.Length)
                                {
                                    AppendPending(buffer.Substring(gtIdx + 1));
                                }
                                yield return new ToolParsedEvent
                                {
                                    Type = ToolParsedEventType.ContentDelta,
                                    Content = headerTag
                                };
                            }
                        }
                        else
                        {
                            // Awaiting end of <invoke name="..."> tag
                            goto LoopEnd;
                        }
                        break;
                    }

                    case State.InInvokeBody:
                    {
                        string trimmed = buffer.TrimStart();
                        int trimOffset = buffer.Length - trimmed.Length;

                        if (trimmed.Length == 0)
                        {
                            // Only whitespace, clear and wait
                            _pendingBuffer.Clear();
                            goto LoopEnd;
                        }

                        if (trimmed.StartsWith(ParamOpenPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            _state = State.InParamHeader;
                            _pendingBuffer.Clear();
                            AppendPending(trimmed);
                        }
                        else if (trimmed.StartsWith(InvokeCloseTag, StringComparison.OrdinalIgnoreCase))
                        {
                            // Tool call closed
                            string finalArgDelta = _isFirstParamInCurrentTool ? "{}" : "}";
                            yield return new ToolParsedEvent
                            {
                                Type = ToolParsedEventType.ToolCallArgumentDelta,
                                ToolIndex = _currentToolIndex,
                                ToolId = _currentToolId,
                                ArgumentDelta = finalArgDelta
                            };

                            _currentToolIndex++;
                            _currentToolId = null;
                            _currentToolName = null;
                            _state = State.NormalContent;

                            int remainingStart = trimOffset + InvokeCloseTag.Length;
                            _pendingBuffer.Clear();
                            if (remainingStart < buffer.Length)
                            {
                                AppendPending(buffer.Substring(remainingStart));
                            }
                        }
                        else
                        {
                            // Check if trimmed starts with a partial prefix of <parameter or </invoke>
                            if (ParamOpenPrefix.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
                                InvokeCloseTag.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                            {
                                _pendingBuffer.Clear();
                                AppendPending(trimmed);
                                goto LoopEnd;
                            }
                            else
                            {
                                // Unrecognized tag or stray text inside invoke body, consume first char to advance
                                _pendingBuffer.Remove(0, trimOffset + 1);
                            }
                        }
                        break;
                    }

                    case State.InParamHeader:
                    {
                        int gtIdx = buffer.IndexOf('>');
                        if (gtIdx >= 0)
                        {
                            string paramTag = buffer.Substring(0, gtIdx + 1);
                            var match = Regex.Match(paramTag, @"<parameter\s+name=[""'](?<pname>[^""']+)[""']\s*>", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                _currentParamName = match.Groups["pname"].Value;
                                string prefix = _isFirstParamInCurrentTool
                                    ? "{\"" + EscapeJsonString(_currentParamName) + "\":\""
                                    : ",\"" + EscapeJsonString(_currentParamName) + "\":\"";

                                _isFirstParamInCurrentTool = false;
                                _state = State.InParamValue;

                                yield return new ToolParsedEvent
                                {
                                    Type = ToolParsedEventType.ToolCallArgumentDelta,
                                    ToolIndex = _currentToolIndex,
                                    ToolId = _currentToolId,
                                    ArgumentDelta = prefix
                                };

                                _pendingBuffer.Clear();
                                if (gtIdx + 1 < buffer.Length)
                                {
                                    AppendPending(buffer.Substring(gtIdx + 1));
                                }
                            }
                            else
                            {
                                _state = State.InInvokeBody;
                                _pendingBuffer.Remove(0, gtIdx + 1);
                            }
                        }
                        else
                        {
                            goto LoopEnd;
                        }
                        break;
                    }

                    case State.InParamValue:
                    {
                        int closeIdx = buffer.IndexOf(ParamCloseTag, StringComparison.OrdinalIgnoreCase);
                        if (closeIdx >= 0)
                        {
                            if (closeIdx > 0)
                            {
                                string valPiece = buffer.Substring(0, closeIdx);
                                yield return new ToolParsedEvent
                                {
                                    Type = ToolParsedEventType.ToolCallArgumentDelta,
                                    ToolIndex = _currentToolIndex,
                                    ToolId = _currentToolId,
                                    ArgumentDelta = EscapeJsonString(valPiece)
                                };
                            }

                            yield return new ToolParsedEvent
                            {
                                Type = ToolParsedEventType.ToolCallArgumentDelta,
                                ToolIndex = _currentToolIndex,
                                ToolId = _currentToolId,
                                ArgumentDelta = "\""
                            };

                            _state = State.InInvokeBody;
                            int remainingStart = closeIdx + ParamCloseTag.Length;
                            _pendingBuffer.Clear();
                            if (remainingStart < buffer.Length)
                            {
                                AppendPending(buffer.Substring(remainingStart));
                            }
                        }
                        else
                        {
                            int partialLen = GetMatchingPrefixSuffixLength(buffer, ParamCloseTag);
                            if (partialLen > 0)
                            {
                                int emitLen = buffer.Length - partialLen;
                                if (emitLen > 0)
                                {
                                    string valPiece = buffer.Substring(0, emitLen);
                                    _pendingBuffer.Clear();
                                    AppendPending(buffer.Substring(emitLen));
                                    yield return new ToolParsedEvent
                                    {
                                        Type = ToolParsedEventType.ToolCallArgumentDelta,
                                        ToolIndex = _currentToolIndex,
                                        ToolId = _currentToolId,
                                        ArgumentDelta = EscapeJsonString(valPiece)
                                    };
                                }
                                goto LoopEnd;
                            }
                            else
                            {
                                int emitLen = GetSafeEmissionLength(buffer);
                                string valPiece = buffer.Substring(0, emitLen);
                                _pendingBuffer.Clear();
                                AppendPending(buffer.Substring(emitLen));
                                if (emitLen > 0)
                                {
                                    yield return new ToolParsedEvent
                                    {
                                        Type = ToolParsedEventType.ToolCallArgumentDelta,
                                        ToolIndex = _currentToolIndex,
                                        ToolId = _currentToolId,
                                        ArgumentDelta = EscapeJsonString(valPiece)
                                    };
                                }
                                goto LoopEnd;
                            }
                        }
                        break;
                    }
                }
            }

        LoopEnd:;
        }

        public IEnumerable<ToolParsedEvent> Flush()
        {
            if (_pendingBuffer.Length > 0)
            {
                string remaining = _pendingBuffer.ToString();
                _pendingBuffer.Clear();

                if (_state == State.NormalContent)
                {
                    yield return new ToolParsedEvent
                    {
                        Type = ToolParsedEventType.ContentDelta,
                        Content = remaining
                    };
                }
                else if (_state == State.InParamValue)
                {
                    yield return new ToolParsedEvent
                    {
                        Type = ToolParsedEventType.ToolCallArgumentDelta,
                        ToolIndex = _currentToolIndex,
                        ToolId = _currentToolId,
                        ArgumentDelta = EscapeJsonString(remaining)
                    };
                }
            }

            if (_state == State.InParamValue)
            {
                var closingEvent = new ToolParsedEvent
                {
                    Type = ToolParsedEventType.ToolCallArgumentDelta,
                    ToolIndex = _currentToolIndex,
                    ToolId = _currentToolId,
                    ArgumentDelta = "\"}"
                };
                _currentToolIndex++;
                ResetCurrentTool();
                yield return closingEvent;
            }
            else if (_state == State.InInvokeBody && _currentToolId != null)
            {
                var closingEvent = new ToolParsedEvent
                {
                    Type = ToolParsedEventType.ToolCallArgumentDelta,
                    ToolIndex = _currentToolIndex,
                    ToolId = _currentToolId,
                    ArgumentDelta = _isFirstParamInCurrentTool ? "{}" : "}"
                };
                _currentToolIndex++;
                ResetCurrentTool();
                yield return closingEvent;
            }
            else if (_state != State.NormalContent)
            {
                ResetCurrentTool();
            }
        }

        private void AppendPending(string text)
        {
            if (text.Length > IncrementalParserLimit.MaxPendingUtf16CodeUnits - _pendingBuffer.Length)
            {
                ResetAfterOverflow();
                throw new IncrementalParserLimitExceededException();
            }

            _pendingBuffer.Append(text);
        }

        private static int GetSafeEmissionLength(string text)
        {
            return char.IsHighSurrogate(text[^1]) ? text.Length - 1 : text.Length;
        }

        private void ResetAfterOverflow()
        {
            _pendingBuffer.Clear();
            _currentToolIndex = 0;
            ResetCurrentTool();
        }

        private void ResetCurrentTool()
        {
            _state = State.NormalContent;
            _currentToolId = null;
            _currentToolName = null;
            _currentParamName = null;
            _isFirstParamInCurrentTool = true;
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

        public static string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            var sb = new StringBuilder(str.Length + 4);
            foreach (char c in str)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        // Escape ASCII control characters (< 0x20) as \u00XX
                        if (c < 0x20)
                        {
                            sb.Append($"\\u{(int)c:X4}");
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
