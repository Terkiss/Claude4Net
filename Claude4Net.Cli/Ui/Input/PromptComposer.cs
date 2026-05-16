using System;

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
                    return new PromptComposerResult(PromptComposerStatus.Submitted, submittedText);
                case InputAction.Cancel:
                    _buffer.Clear();
                    _history.ResetIndex();
                    return new PromptComposerResult(PromptComposerStatus.Cancelled, null);
                case InputAction.ClearScreen:
                    return new PromptComposerResult(PromptComposerStatus.ClearSignal, null);
            }

            return new PromptComposerResult(PromptComposerStatus.Editing, null);
        }

        /// <summary>
        /// Manually sets the buffer text (primarily for testing).
        /// </summary>
        public void SetBuffer(string text)
        {
            _buffer.SetText(text);
        }
    }
}
