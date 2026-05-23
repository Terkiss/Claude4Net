using System;
using System.Collections.Generic;
using System.Linq;
using Claude4Net.Commands;

namespace Claude4Net.Cli.Ui.Input
{
    /// <summary>
    /// Orchestrates CLI input components: buffer, history, and suggestions.
    /// </summary>
    public sealed class PromptComposer
    {
        private readonly PromptBuffer _buffer = new();
        private readonly PromptHistory _history = new();
        private readonly CommandSuggester _suggester = new();
        private readonly KeyBindingRegistry _keyBindings = new();

        public bool IsCommandPaletteVisible { get; set; }
        public string PaletteFilterText { get; set; } = string.Empty;
        public int PaletteSelectedIndex { get; set; }

        /// <summary>
        /// Gets current composer state (text, cursor, suggestion).
        /// </summary>
        public PromptComposerState GetState()
        {
            var text = _buffer.Text;
            var suggestion = _suggester.GetSuggestion(text);
            return new PromptComposerState(text, _buffer.CursorPosition, suggestion);
        }

        /// <summary>
        /// Processes a key and returns the result status.
        /// </summary>
        public PromptComposerResult ProcessKey(ConsoleKeyInfo keyInfo)
        {
            if (IsCommandPaletteVisible)
            {
                var filteredCommands = GetFilteredCommands();
                int N = filteredCommands.Count;

                if (keyInfo.Key == ConsoleKey.UpArrow)
                {
                    if (N > 0)
                    {
                        PaletteSelectedIndex = (PaletteSelectedIndex - 1 + N) % N;
                    }
                    return new PromptComposerResult(PromptComposerStatus.Editing, null, InputAction.None);
                }
                else if (keyInfo.Key == ConsoleKey.DownArrow)
                {
                    if (N > 0)
                    {
                        PaletteSelectedIndex = (PaletteSelectedIndex + 1) % N;
                    }
                    return new PromptComposerResult(PromptComposerStatus.Editing, null, InputAction.None);
                }
                else if (keyInfo.Key == ConsoleKey.Escape)
                {
                    IsCommandPaletteVisible = false;
                    UpdatePaletteState();
                    return new PromptComposerResult(PromptComposerStatus.Editing, null, InputAction.None);
                }
                else if (keyInfo.Key == ConsoleKey.Enter)
                {
                    if (N > 0 && PaletteSelectedIndex >= 0 && PaletteSelectedIndex < N)
                    {
                        var selectedCommand = filteredCommands[PaletteSelectedIndex];
                        _buffer.SetText("/" + selectedCommand.Name);
                    }
                    IsCommandPaletteVisible = false;
                    UpdatePaletteState();
                    return new PromptComposerResult(PromptComposerStatus.Editing, null, InputAction.None);
                }
            }

            var action = _keyBindings.GetAction(keyInfo);

            switch (action)
            {
                case InputAction.InsertChar:
                    _buffer.Insert(keyInfo.KeyChar);
                    break;
                case InputAction.Backspace:
                    _buffer.Backspace();
                    break;
                case InputAction.Delete:
                    _buffer.Delete();
                    break;
                case InputAction.MoveLeft:
                    _buffer.MoveLeft();
                    break;
                case InputAction.MoveRight:
                    _buffer.MoveRight();
                    break;
                case InputAction.MoveHome:
                    _buffer.MoveHome();
                    break;
                case InputAction.MoveEnd:
                    _buffer.MoveEnd();
                    break;
                case InputAction.HistoryUp:
                    var up = _history.NavigateUp(_buffer.Text);
                    if (up != null) _buffer.SetText(up);
                    break;
                case InputAction.HistoryDown:
                    var down = _history.NavigateDown();
                    if (down != null) _buffer.SetText(down);
                    break;
                case InputAction.ApplySuggestion:
                    var suggestion = _suggester.GetSuggestion(_buffer.Text);
                    if (suggestion != null) _buffer.SetText(suggestion);
                    break;
                case InputAction.Submit:
                    var submittedText = _buffer.Text;
                    if (!string.IsNullOrWhiteSpace(submittedText))
                    {
                        _history.Add(submittedText);
                    }
                    _buffer.Clear();
                    _history.ResetIndex();
                    UpdatePaletteState();
                    return new PromptComposerResult(PromptComposerStatus.Submitted, submittedText);
                case InputAction.Cancel:
                    _buffer.Clear();
                    _history.ResetIndex();
                    UpdatePaletteState();
                    return new PromptComposerResult(PromptComposerStatus.Cancelled, null);
                case InputAction.ClearScreen:
                    UpdatePaletteState();
                    return new PromptComposerResult(PromptComposerStatus.ClearSignal, null, action);
                case InputAction.ScrollUp:
                case InputAction.ScrollDown:
                case InputAction.ScrollToHome:
                case InputAction.ScrollToEnd:
                    UpdatePaletteState();
                    return new PromptComposerResult(PromptComposerStatus.Scrolled, null, action);
            }

            // Detect new '/' input:
            if (keyInfo.KeyChar == '/' && _buffer.Text.StartsWith("/"))
            {
                IsCommandPaletteVisible = true;
                PaletteSelectedIndex = 0;
            }

            UpdatePaletteState();

            return new PromptComposerResult(PromptComposerStatus.Editing, null, action);
        }

        private List<Command> GetFilteredCommands()
        {
            var commands = CommandRegistry.GetCommands()
                .OrderBy(c => c.Name)
                .ToList();

            if (string.IsNullOrEmpty(PaletteFilterText))
            {
                return commands;
            }

            return commands
                .Where(c => c.Name.StartsWith(PaletteFilterText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private void UpdatePaletteState()
        {
            if (!_buffer.Text.StartsWith("/"))
            {
                IsCommandPaletteVisible = false;
            }

            if (IsCommandPaletteVisible)
            {
                PaletteFilterText = _buffer.Text.Length > 1 ? _buffer.Text.Substring(1) : string.Empty;
                var filtered = GetFilteredCommands();
                if (filtered.Count == 0)
                {
                    PaletteSelectedIndex = 0;
                }
                else if (PaletteSelectedIndex >= filtered.Count)
                {
                    PaletteSelectedIndex = filtered.Count - 1;
                }
            }
            else
            {
                PaletteFilterText = string.Empty;
                PaletteSelectedIndex = 0;
            }
        }

        /// <summary>
        /// Manually sets the buffer text (primarily for testing).
        /// </summary>
        public void SetBuffer(string text)
        {
            _buffer.SetText(text);
            UpdatePaletteState();
        }
    }
}
