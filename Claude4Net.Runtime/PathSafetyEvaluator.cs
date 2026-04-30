using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public enum PathSafetyResult
    {
        NotApplicable,
        SafeSystem,
        Workspace,
        Outside
    }

    public class PathSafetyEvaluator
    {
        public PathSafetyResult EvaluateInputSafety(object? input)
        {
            if (input == null) return PathSafetyResult.NotApplicable;

            try
            {
                var json = JsonSerializer.Serialize(input);
                using var doc = JsonDocument.Parse(json);
                return CheckElementSafety(doc.RootElement);
            }
            catch { return PathSafetyResult.Outside; }
        }

        private PathSafetyResult CheckElementSafety(JsonElement element)
        {
            PathSafetyResult minSafety = PathSafetyResult.NotApplicable;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    PathSafetyResult s;
                    if (prop.Name.Equals("command", StringComparison.OrdinalIgnoreCase) || 
                        prop.Name.Equals("sql", StringComparison.OrdinalIgnoreCase))
                    {
                        s = CheckCommandSafety(prop.Value.GetString());
                    }
                    else
                    {
                        s = CheckElementSafety(prop.Value);
                    }
                    minSafety = GetMinSafety(minSafety, s);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    PathSafetyResult s = CheckElementSafety(item);
                    minSafety = GetMinSafety(minSafety, s);
                }
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                PathSafetyResult s = EvaluateSinglePathSafety(element.GetString());
                minSafety = GetMinSafety(minSafety, s);
            }

            return minSafety;
        }

        private PathSafetyResult CheckCommandSafety(string? cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return PathSafetyResult.NotApplicable;

            string[] tokens = cmd.Split(new[] { ' ', '\t', '|', '>', '<', '&', ';' }, StringSplitOptions.RemoveEmptyEntries);
            PathSafetyResult maxRisk = PathSafetyResult.NotApplicable;

            foreach (var token in tokens)
            {
                string t = token.Trim('\'', '\"');
                
                // If it's a CLI flag (/f), we skip path evaluation
                if (t.StartsWith("/") && t.Length <= 10 && !t.Contains("\\") && t.LastIndexOf('/') == 0)
                    continue;

                PathSafetyResult s = EvaluateSinglePathSafety(t);
                maxRisk = GetMinSafety(maxRisk, s);
                if (maxRisk == PathSafetyResult.Outside) return PathSafetyResult.Outside;
            }
            return maxRisk == PathSafetyResult.NotApplicable ? PathSafetyResult.Workspace : maxRisk; 
        }

        public PathSafetyResult EvaluateSinglePathSafety(string? targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return PathSafetyResult.NotApplicable;

            // Heuristic to distinguish between Windows-style CLI flags and paths starting with '/'
            if (targetPath.StartsWith("/"))
            {
                bool hasInternalSlash = targetPath.IndexOf('/', 1) > 0;
                bool isShortAlphanumeric = targetPath.Length <= 10 && targetPath.Substring(1).All(char.IsLetterOrDigit);
                
                // If it looks like a flag, it's not a path we evaluate here.
                if (!hasInternalSlash && isShortAlphanumeric) return PathSafetyResult.NotApplicable;
            }

            try
            {
                if (targetPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    targetPath = new Uri(targetPath).LocalPath;
                }

                if (!targetPath.Contains(Path.DirectorySeparatorChar) && 
                    !targetPath.Contains(Path.AltDirectorySeparatorChar) && 
                    !targetPath.Contains("..")) return PathSafetyResult.NotApplicable;

                string fullPath = Path.GetFullPath(targetPath);
                
                bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                var comparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

                // 1. Check Restricted System Storage (db and Skills only)
                string sysPath = Path.GetFullPath(AppState.SystemBaseDir);
                string normSys = sysPath.EndsWith(Path.DirectorySeparatorChar) ? sysPath : sysPath + Path.DirectorySeparatorChar;

                if (fullPath.StartsWith(normSys, comparison) || fullPath.Equals(sysPath, comparison))
                {
                    if (fullPath.Contains($"{Path.DirectorySeparatorChar}db{Path.DirectorySeparatorChar}") || 
                        fullPath.Contains($"{Path.DirectorySeparatorChar}Skills{Path.DirectorySeparatorChar}") ||
                        fullPath.EndsWith($"{Path.DirectorySeparatorChar}db") ||
                        fullPath.EndsWith($"{Path.DirectorySeparatorChar}Skills"))
                    {
                        return PathSafetyResult.SafeSystem;
                    }
                    return PathSafetyResult.Outside; 
                }

                // 2. Check User Workspace
                if (!string.IsNullOrEmpty(AppState.CurrentCwd))
                {
                    string wsPath = Path.GetFullPath(AppState.CurrentCwd);
                    string normWs = wsPath.EndsWith(Path.DirectorySeparatorChar) ? wsPath : wsPath + Path.DirectorySeparatorChar;
                    
                    if (fullPath.StartsWith(normWs, comparison) || fullPath.Equals(wsPath, comparison))
                    {
                        if (AppState.CurrentPermissionMode != PermissionMode.Yolo)
                        {
                            if (fullPath.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") || 
                                fullPath.Contains($"{Path.DirectorySeparatorChar}.gemini{Path.DirectorySeparatorChar}"))
                                return PathSafetyResult.Outside;
                        }
                        return PathSafetyResult.Workspace;
                    }
                }

                return PathSafetyResult.Outside;
            }
            catch { return PathSafetyResult.Outside; } 
        }

        private PathSafetyResult GetMinSafety(PathSafetyResult current, PathSafetyResult @new)
        {
            // Severity: Outside > Workspace > SafeSystem > NotApplicable
            // But NotApplicable is special, if everything is NotApplicable, it's NotApplicable.
            // If one is Outside, it's Outside.
            
            if (current == PathSafetyResult.Outside || @new == PathSafetyResult.Outside) return PathSafetyResult.Outside;
            if (current == PathSafetyResult.Workspace || @new == PathSafetyResult.Workspace) return PathSafetyResult.Workspace;
            if (current == PathSafetyResult.SafeSystem || @new == PathSafetyResult.SafeSystem) return PathSafetyResult.SafeSystem;
            return PathSafetyResult.NotApplicable;
        }
    }
}
