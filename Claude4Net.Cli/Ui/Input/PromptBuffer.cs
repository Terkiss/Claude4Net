using System;

namespace Claude4Net.Cli.Ui.Input
{
    /// <summary>
    /// Manages the text buffer and cursor position for CLI input.
    /// </summary>
    public sealed class PromptBuffer
    {
        private string _text = string.Empty;
        private int _cursorPosition = 0;

        public string Text => _text;
        public int CursorPosition => _cursorPosition;

        public void Insert(char c)
        {
            _text = _text.Insert(_cursorPosition, c.ToString());
            _cursorPosition++;
        }

        public void Insert(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _text = _text.Insert(_cursorPosition, text);
            _cursorPosition += text.Length;
        }

        public void Backspace()
        {
            if (_cursorPosition > 0)
            {
                _text = _text.Remove(_cursorPosition - 1, 1);
                _cursorPosition--;
            }
        }

        public void Delete()
        {
            if (_cursorPosition < _text.Length)
            {
                _text = _text.Remove(_cursorPosition, 1);
            }
        }

        public void MoveLeft()
        {
            if (_cursorPosition > 0) _cursorPosition--;
        }

        public void MoveRight()
        {
            if (_cursorPosition < _text.Length) _cursorPosition++;
        }

        public void MoveHome()
        {
            _cursorPosition = 0;
        }

        public void MoveEnd()
        {
            _cursorPosition = _text.Length;
        }

        public void SetText(string text, int? cursor = null)
        {
            _text = text ?? string.Empty;
            _cursorPosition = cursor ?? _text.Length;
            if (_cursorPosition > _text.Length) _cursorPosition = _text.Length;
            if (_cursorPosition < 0) _cursorPosition = 0;
        }

        public void Clear()
        {
            _text = string.Empty;
            _cursorPosition = 0;
        }
    }
}
