namespace Claude4Net.Cli.Ui;

/// <summary>
/// Defines the color palette and styling for Project Lumen.
/// These strings are intended for use with Spectre.Console markup.
/// </summary>
public static class LumenTheme
{
    public static string UserColor { get; set; } = "green";
    public static string AssistantColor { get; set; } = "cyan";
    public static string ThinkingColor { get; set; } = "grey";
    public static string ToolColor { get; set; } = "yellow";
    public static string SuccessColor { get; set; } = "green";
    public static string WarningColor { get; set; } = "orange1";
    public static string ErrorColor { get; set; } = "red";
    public static string MetadataColor { get; set; } = "blue";
    public static string BorderColor { get; set; } = "grey35";
    public static string PromptSymbol { get; set; } = "> ";

    public static void ApplyTheme(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "light":
                UserColor = "darkgreen";
                AssistantColor = "navy";
                ThinkingColor = "silver";
                ToolColor = "darkgoldenrod";
                SuccessColor = "darkgreen";
                WarningColor = "darkorange";
                ErrorColor = "darkred";
                MetadataColor = "teal";
                BorderColor = "grey70";
                PromptSymbol = "> ";
                break;
            case "neon":
                UserColor = "springgreen1";
                AssistantColor = "magenta1";
                ThinkingColor = "grey58";
                ToolColor = "gold1";
                SuccessColor = "lime";
                WarningColor = "darkorange1";
                ErrorColor = "red1";
                MetadataColor = "deepskyblue1";
                BorderColor = "purple";
                PromptSymbol = "» ";
                break;
            case "dark":
            default:
                UserColor = "green";
                AssistantColor = "cyan";
                ThinkingColor = "grey";
                ToolColor = "yellow";
                SuccessColor = "green";
                WarningColor = "orange1";
                ErrorColor = "red";
                MetadataColor = "blue";
                BorderColor = "grey35";
                PromptSymbol = "> ";
                break;
        }
    }
}
