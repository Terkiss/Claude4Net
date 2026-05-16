using System;
using System.Collections.Generic;
using Claude4Net.Cli.Ui.Input;
using Xunit;

namespace Claude4Net.Tests
{
    public class K043PromptComposerTests
    {
        [Fact]
        public void AppendsCharacters()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false));

            var state = composer.GetState();
            Assert.Equal("ab", state.Text);
            Assert.Equal(2, state.CursorPosition);
        }

        [Fact]
        public void BackspaceRemovesCharacterBeforeCursor()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));

            var state = composer.GetState();
            Assert.Equal("a", state.Text);
            Assert.Equal(1, state.CursorPosition);
        }

        [Fact]
        public void DeleteRemovesCharacterAtCursor()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false));
            // Move cursor back one
            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
            // Delete 'b'
            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false));

            var state = composer.GetState();
            Assert.Equal("a", state.Text);
            Assert.Equal(1, state.CursorPosition);
        }

        [Fact]
        public void LeftRightMovesCursorWithinBounds()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false));

            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
            Assert.Equal(1, composer.GetState().CursorPosition);

            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
            Assert.Equal(2, composer.GetState().CursorPosition);

            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false)); // Out of bounds
            Assert.Equal(2, composer.GetState().CursorPosition);
        }

        [Fact]
        public void HomeEndMovesCursor()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false));

            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
            Assert.Equal(0, composer.GetState().CursorPosition);

            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
            Assert.Equal(2, composer.GetState().CursorPosition);
        }

        [Fact]
        public void UpDownNavigatePromptHistory()
        {
            var composer = new PromptComposer();
            // History 1
            composer.ProcessKey(new ConsoleKeyInfo('h', ConsoleKey.H, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

            // History 2
            composer.ProcessKey(new ConsoleKeyInfo('h', ConsoleKey.H, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

            // Up to "h2"
            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
            Assert.Equal("h2", composer.GetState().Text);

            // Up to "h1"
            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
            Assert.Equal("h1", composer.GetState().Text);

            // Down to "h2"
            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
            Assert.Equal("h2", composer.GetState().Text);

            // Down to empty (temporary buffer)
            composer.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
            Assert.Equal("", composer.GetState().Text);
        }

        [Fact]
        public void TabAppliesCommandSuggestion()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('/', ConsoleKey.Divide, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('h', ConsoleKey.H, false, false, false));
            composer.ProcessKey(new ConsoleKeyInfo('e', ConsoleKey.E, false, false, false));

            // Suggestion should be "/help"
            Assert.Equal("/help", composer.GetState().Suggestion);

            composer.ProcessKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));
            Assert.Equal("/help", composer.GetState().Text);
        }

        [Fact]
        public void EnterReturnsSubmitResult()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('t', ConsoleKey.T, false, false, false));
            var result = composer.ProcessKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

            Assert.Equal(PromptComposerStatus.Submitted, result.Status);
            Assert.Equal("t", result.Text);
            Assert.Equal("", composer.GetState().Text);
        }

        [Fact]
        public void EscapeReturnsCancelResult()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('t', ConsoleKey.T, false, false, false));
            var result = composer.ProcessKey(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));

            Assert.Equal(PromptComposerStatus.Cancelled, result.Status);
            Assert.Equal("", composer.GetState().Text);
        }

        [Fact]
        public void CtrlCReturnsCancelResult()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('t', ConsoleKey.T, false, false, false));
            var result = composer.ProcessKey(new ConsoleKeyInfo('C', ConsoleKey.C, false, false, true)); // Ctrl+C

            Assert.Equal(PromptComposerStatus.Cancelled, result.Status);
            Assert.Equal("", composer.GetState().Text);
        }

        [Fact]
        public void CtrlLReturnsClearResult()
        {
            var composer = new PromptComposer();
            var result = composer.ProcessKey(new ConsoleKeyInfo('L', ConsoleKey.L, false, false, true)); // Ctrl+L

            Assert.Equal(PromptComposerStatus.ClearSignal, result.Status);
        }

        [Fact]
        public void SnapshotExposesTextAndCursor()
        {
            var composer = new PromptComposer();
            composer.ProcessKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));

            var state = composer.GetState();
            Assert.Equal("a", state.Text);
            Assert.Equal(1, state.CursorPosition);
        }

        [Fact]
        public void CommandSuggestionsIncludeExistingSlashBangCommandsWithoutExecutingHandlers()
        {
            var suggester = new CommandSuggester();
            // "help" command exists in CommandRegistry
            var suggestion = suggester.GetSuggestion("/he");
            Assert.Equal("/help", suggestion);

            var suggestion2 = suggester.GetSuggestion("!he");
            Assert.Equal("!help", suggestion2);
        }
    }
}
