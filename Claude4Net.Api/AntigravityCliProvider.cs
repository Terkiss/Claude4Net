using System;
using Claude4Net.SDK;

namespace Claude4Net.Api
{
    public class AntigravityCliProvider : GeminiCliProvider
    {
        public AntigravityCliProvider(IToolRegistry toolRegistry) : base(toolRegistry)
        {
        }

        public override string Name => "antigravity-cli";
    }
}
