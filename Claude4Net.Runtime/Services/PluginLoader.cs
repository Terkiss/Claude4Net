using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Runtime.Services
{
    public class PluginLoader
    {
        private readonly IServiceProvider _serviceProvider;

        public PluginLoader(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public List<ITool> LoadToolsFromDirectory(string directoryPath)
        {
            var tools = new List<ITool>();
            if (!Directory.Exists(directoryPath)) return tools;

            foreach (var dllPath in Directory.GetFiles(directoryPath, "*.dll"))
            {
                try
                {
                    byte[] rawAssembly = File.ReadAllBytes(dllPath);
                    var assembly = Assembly.Load(rawAssembly);
                    var toolTypes = assembly.GetTypes()
                        .Where(t => typeof(ITool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                    foreach (var type in toolTypes)
                    {
                        var instance = ActivatorUtilities.CreateInstance(_serviceProvider, type) as ITool;
                        if (instance != null) tools.Add(instance);
                    }
                }
                catch
                {
                    // Ignore invalid plugin DLLs and keep the host available.
                }
            }
            return tools;
        }
    }
}
