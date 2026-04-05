using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    public class LspPosition
    {
        public int Line { get; set; }
        public int Character { get; set; }
    }

    public class LspRange
    {
        public LspPosition Start { get; set; } = new();
        public LspPosition End { get; set; } = new();
    }

    public class LspLocation
    {
        public string Uri { get; set; } = string.Empty;
        public LspRange Range { get; set; } = new();
    }

    public class LspLocationLink
    {
        public LspRange? OriginSelectionRange { get; set; }
        public string TargetUri { get; set; } = string.Empty;
        public LspRange TargetRange { get; set; } = new();
        public LspRange TargetSelectionRange { get; set; } = new();
    }

    public class LspSymbolInformation
    {
        public string Name { get; set; } = string.Empty;
        public int Kind { get; set; }
        public LspLocation Location { get; set; } = new();
        public string? ContainerName { get; set; }
    }

    public class LspDocumentSymbol
    {
        public string Name { get; set; } = string.Empty;
        public string? Detail { get; set; }
        public int Kind { get; set; }
        public LspRange Range { get; set; } = new();
        public LspRange SelectionRange { get; set; } = new();
        public List<LspDocumentSymbol>? Children { get; set; }
    }

    public class LspHover
    {
        public object Contents { get; set; } = new(); // MarkupContent or string
        public LspRange? Range { get; set; }
    }
}
