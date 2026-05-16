using System;

namespace Claude4Net.Cli.Ui.Input
{
    /// <summary>
    /// Enumeration of possible input actions.
    /// </summary>
    public enum InputAction
    {
        None,
        InsertChar,
        Backspace,
        Delete,
        MoveLeft,
        MoveRight,
        MoveHome,
        MoveEnd,
        HistoryUp,
        HistoryDown,
        ApplySuggestion,
        Submit,
        Cancel,
        ClearScreen
    }

    /// <summary>
    /// Maps keys to actions.
    /// </summary>
    public sealed class KeyBindingRegistry
    {
        public InputAction GetAction(ConsoleKeyInfo keyInfo)
        {
            // Handle Control combinations
            if ((keyInfo.Modifiers & ConsoleModifiers.Control) != 0)
            {
                return keyInfo.Key switch
                {
                    ConsoleKey.C => InputAction.Cancel,
                    ConsoleKey.L => InputAction.ClearScreen,
                    _ => InputAction.None
                };
            }

            // Handle standard keys
            return keyInfo.Key switch
            {
                ConsoleKey.Backspace => InputAction.Backspace,
                ConsoleKey.Delete => InputAction.Delete,
                ConsoleKey.LeftArrow => InputAction.MoveLeft,
                ConsoleKey.RightArrow => InputAction.MoveRight,
                ConsoleKey.Home => InputAction.MoveHome,
                ConsoleKey.End => InputAction.MoveEnd,
                ConsoleKey.UpArrow => InputAction.HistoryUp,
                ConsoleKey.DownArrow => InputAction.HistoryDown,
                ConsoleKey.Tab => InputAction.ApplySuggestion,
                ConsoleKey.Enter => InputAction.Submit,
                ConsoleKey.Escape => InputAction.Cancel,
                _ => !char.IsControl(keyInfo.KeyChar) ? InputAction.InsertChar : InputAction.None
            };
        }
    }
}
