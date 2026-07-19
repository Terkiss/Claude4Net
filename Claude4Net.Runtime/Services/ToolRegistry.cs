using System;
using System.Collections.Generic;
using System.Linq;
using Claude4Net.SDK;

namespace Claude4Net.Runtime.Services
{
    public class ToolRegistry : IToolRegistry
    {
        private readonly List<ITool> _coreTools;
        private readonly List<ITool> _dynamicTools = new();

        public ToolRegistry(IEnumerable<ITool> coreTools)
        {
            _coreTools = coreTools.ToList();
        }

        public void AddTool(ITool tool)
        {
            if (!_coreTools.Any(t => t.Name == tool.Name)) _coreTools.Add(tool);
        }

        public void SetDynamicTools(IEnumerable<ITool> tools)
        {
            _dynamicTools.Clear();
            _dynamicTools.AddRange(tools);
        }

        public IReadOnlyList<ITool> GetTools() => _coreTools.Concat(_dynamicTools).ToList();

        public ITool? GetTool(string name)
        {
            return _coreTools.Concat(_dynamicTools).FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                (t.Aliases != null && t.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase))));
        }
    }
}
