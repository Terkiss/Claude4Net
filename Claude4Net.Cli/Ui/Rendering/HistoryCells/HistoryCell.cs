using System.Text;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public abstract class HistoryCell
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>
    /// Returns a plain text representation of the cell for deterministic testing.
    /// </summary>
    public abstract string ToPlainText();

    /// <summary>
    /// Appends delta text to the cell if it supports streaming.
    /// </summary>
    public virtual void AppendDelta(string delta) { }
}
