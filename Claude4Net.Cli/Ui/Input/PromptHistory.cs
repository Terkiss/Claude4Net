using System.Collections.Generic;

namespace Claude4Net.Cli.Ui.Input
{
    /// <summary>
    /// Manages history of entered prompts for up/down navigation.
    /// </summary>
    public sealed class PromptHistory
    {
        private readonly List<string> _history = new();
        private int _index = -1;
        private string _temporaryBuffer = string.Empty;

        public void Add(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return;
            // Prevent duplicate entries
            if (_history.Count > 0 && _history[_history.Count - 1] == prompt) return;

            _history.Add(prompt);
            _index = -1;
        }

        public string? NavigateUp(string currentBuffer)
        {
            if (_history.Count == 0) return null;

            if (_index == -1)
            {
                _temporaryBuffer = currentBuffer;
                _index = _history.Count - 1;
            }
            else if (_index > 0)
            {
                _index--;
            }
            else
            {
                // Already at the top
                return _history[0];
            }

            return _history[_index];
        }

        public string? NavigateDown()
        {
            if (_index == -1) return null;

            _index++;
            if (_index >= _history.Count)
            {
                _index = -1;
                return _temporaryBuffer;
            }

            return _history[_index];
        }

        public void ResetIndex()
        {
            _index = -1;
            _temporaryBuffer = string.Empty;
        }
    }
}
