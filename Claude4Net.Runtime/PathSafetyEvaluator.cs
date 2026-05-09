using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// ê²½ë¡œ ?ˆì „???‰ê? ê²°ê³¼ ?´ê±°?•ì…?ˆë‹¤.
    /// </summary>
    public enum PathSafetyResult
    {
        /// <summary> ê²½ë¡œ ?‰ê?ê°€ ?´ë‹¹?˜ì? ?ŠìŒ (?¼ë°˜ ?ìŠ¤???? </summary>
        NotApplicable,
        /// <summary> ?œìŠ¤??ë³´í˜¸ êµ¬ì—­ (db, Skills ?´ë” ?? ?´ì˜ ?ˆì „???‘ê·¼ </summary>
        SafeSystem,
        /// <summary> ?¤ì •???‘ì—… ê³µê°„(Workspace) ?´ì˜ ?•ìƒ?ì¸ ?‘ê·¼ </summary>
        Workspace,
        /// <summary> ?ˆìš©?˜ì? ?Šì? ?¸ë? ê²½ë¡œ ?ëŠ” ê¸ˆì???êµ¬ì—­ ?‘ê·¼ </summary>
        Outside
    }

    /// <summary>
    /// ?„êµ¬???…ë ¥??ê²½ë¡œ???ˆì „?±ì„ ?‰ê??˜ëŠ” ?”ì§„?…ë‹ˆ??
    /// ?ì´?„íŠ¸ê°€ ?ˆìš©???Œë“œë°•ìŠ¤(Workspace) ?¸ë????Œì¼???‘ê·¼?˜ê±°???œìŠ¤???Œì¼???ìƒ?œí‚¤??ê²ƒì„ ë°©ì??©ë‹ˆ??
    /// </summary>
    public class PathSafetyEvaluator
    {
        /// <summary>
        /// ?„êµ¬???…ë ¥ ê°ì²´ ?„ì²´ë¥??¬ê??ìœ¼ë¡??ìƒ‰?˜ì—¬ ?¬í•¨??ëª¨ë“  ê²½ë¡œ???ˆì „?±ì„ ?‰ê??©ë‹ˆ??
        /// </summary>
        /// <param name="input">?„êµ¬ ?¤í–‰???¬ìš©???…ë ¥ ?°ì´??/param>
        /// <returns>?ìƒ‰??ê²½ë¡œ ì¤?ê°€???„í—˜???˜ì???ê²°ê³¼</returns>
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

        /// <summary>
        /// JSON ?”ì†Œë¥??¬ê??ìœ¼ë¡?ë¶„ì„?˜ì—¬ ê²½ë¡œ ?ëŠ” ëª…ë ¹???„ë“œë¥??ë³„?˜ê³  ê²€?¬í•©?ˆë‹¤.
        /// </summary>
        private PathSafetyResult CheckElementSafety(JsonElement element)
        {
            PathSafetyResult minSafety = PathSafetyResult.NotApplicable;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    PathSafetyResult s;
                    // ëª…ë ¹???¤í–‰(Bash ???´ë‚˜ SQL ì¿¼ë¦¬ ?´ë???ê²½ë¡œ??ê°€ë¡œì±„??ê²€??
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

        /// <summary>
        /// ??ëª…ë ¹??ë¬¸ì?´ì„ ? í°?”í•˜??ê°??¸ìê°€ ?ˆì „??ê²½ë¡œ?¸ì? ê²€?¬í•©?ˆë‹¤.
        /// </summary>
        private PathSafetyResult CheckCommandSafety(string? cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return PathSafetyResult.NotApplicable;

            // ê³µë°± ë°????œì–´ ë¬¸ì ?¨ìœ„ë¡?ë¶„ë¦¬
            string[] tokens = cmd.Split(new[] { ' ', '\t', '|', '>', '<', '&', ';' }, StringSplitOptions.RemoveEmptyEntries);
            PathSafetyResult maxRisk = PathSafetyResult.NotApplicable;

            foreach (var token in tokens)
            {
                string t = token.Trim('\'', '\"');

                // Windows ?¤í??¼ì˜ CLI ?Œë˜ê·?/f, /y ????ê²½ë¡œ ê²€?¬ì—???œì™¸?˜ëŠ” ?´ë¦¬?¤í‹± ?ìš©
                if (t.StartsWith("/") && t.Length <= 10 && !t.Contains("\\") && t.LastIndexOf('/') == 0)
                    continue;

                PathSafetyResult s = EvaluateSinglePathSafety(t);
                maxRisk = GetMinSafety(maxRisk, s);
                if (maxRisk == PathSafetyResult.Outside) return PathSafetyResult.Outside;
            }
            return maxRisk == PathSafetyResult.NotApplicable ? PathSafetyResult.Workspace : maxRisk;
        }

        /// <summary>
        /// ?¨ì¼ ë¬¸ì?´ì´ ?˜í??´ëŠ” ê²½ë¡œê°€ ?Œë“œë°•ìŠ¤ ?•ì±…??ì¤€?˜í•˜?”ì? ?‰ê??©ë‹ˆ??
        /// </summary>
        /// <param name="targetPath">ê²€?¬í•  ?€??ê²½ë¡œ ë¬¸ì??/param>
        public PathSafetyResult EvaluateSinglePathSafety(string? targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return PathSafetyResult.NotApplicable;

            // CLI ?Œë˜ê·??¬ë? ?¬í™•??(?´ë¦¬?¤í‹±)
            if (targetPath.StartsWith("/"))
            {
                bool hasInternalSlash = targetPath.IndexOf('/', 1) > 0;
                bool isShortAlphanumeric = targetPath.Length <= 10 && targetPath.Substring(1).All(char.IsLetterOrDigit);

                if (!hasInternalSlash && isShortAlphanumeric) return PathSafetyResult.NotApplicable;
            }

            try
            {
                // URI ?•ì‹ ì²˜ë¦¬
                if (targetPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    targetPath = new Uri(targetPath).LocalPath;
                }

                // OSë³?ê²½ë¡œ êµ¬ë¶„???•ê·œ??(?¬ë˜????Š¬?˜ì‹œ ?¼ìš© ?€??
                string normalizedInput = targetPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

                // ?¨ìˆœ ?Œì¼ëª…ë§Œ ?ˆê±°??ê²½ë¡œ êµ¬ë¶„?ê? ?†ëŠ” ê²½ìš° ?ë? ê²½ë¡œë¡?ê°„ì£¼?˜ê³  ?ˆìš© ?¬ë? ? ë³´
                if (!normalizedInput.Contains(Path.DirectorySeparatorChar) &&
                    !normalizedInput.Contains("..")) return PathSafetyResult.NotApplicable;

                // ?ˆë? ê²½ë¡œë¡?ë³€?˜í•˜???ë? ê²½ë¡œ ?°íšŒ(Traversal) ë°©ì?
                string fullPath = ResolveFinalPath(Path.GetFullPath(targetPath));

                bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                var comparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

                // 1. ?œìŠ¤???´ë? ë³´í˜¸ êµ¬ì—­(db, Skills) ì²´í¬
                string sysPath = Path.GetFullPath(AppState.SystemBaseDir);
                string normSys = EnsureTrailingSeparator(sysPath);

                if (fullPath.StartsWith(normSys, comparison) || fullPath.Equals(sysPath.TrimEnd(Path.DirectorySeparatorChar), comparison))
                {
                    // ?œìŠ¤???´ë” ?´ì—?œë„ ?¤ì§ db?€ Skills ?´ë”ë§??‘ê·¼ ?ˆìš©
                    string dbSeg = $"{Path.DirectorySeparatorChar}db{Path.DirectorySeparatorChar}";
                    string skSeg = $"{Path.DirectorySeparatorChar}Skills{Path.DirectorySeparatorChar}";

                    if (fullPath.Contains(dbSeg, comparison) ||
                        fullPath.Contains(skSeg, comparison) ||
                        fullPath.EndsWith($"{Path.DirectorySeparatorChar}db", comparison) ||
                        fullPath.EndsWith($"{Path.DirectorySeparatorChar}Skills", comparison))
                    {
                        return PathSafetyResult.SafeSystem;
                    }
                    return PathSafetyResult.Outside;
                }

                // 2. ?¬ìš©???‘ì—… ê³µê°„(Workspace) ì²´í¬
                if (!string.IsNullOrEmpty(AppState.CurrentCwd))
                {
                    string wsPath = Path.GetFullPath(AppState.CurrentCwd);
                    string normWs = EnsureTrailingSeparator(wsPath);

                    if (fullPath.StartsWith(normWs, comparison) || fullPath.Equals(wsPath.TrimEnd(Path.DirectorySeparatorChar), comparison))
                    {
                        // YOLO ëª¨ë“œê°€ ?„ë‹ ê²½ìš° .git?´ë‚˜ .gemini ê°™ì? ë¯¼ê°???´ë? ?´ë” ?‘ê·¼ ì°¨ë‹¨
                        if (AppState.CurrentPermissionMode != PermissionMode.Yolo)
                        {
                            string gitSeg = $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}";
                            string gemSeg = $"{Path.DirectorySeparatorChar}.gemini{Path.DirectorySeparatorChar}";

                            if (fullPath.Contains(gitSeg, comparison) ||
                                fullPath.Contains(gemSeg, comparison) ||
                                fullPath.EndsWith($"{Path.DirectorySeparatorChar}.git", comparison) ||
                                fullPath.EndsWith($"{Path.DirectorySeparatorChar}.gemini", comparison))
                                return PathSafetyResult.Outside;
                        }
                        return PathSafetyResult.Workspace;
                    }
                }

                // ?´ë””?ë„ ?í•˜ì§€ ?Šìœ¼ë©??¸ë? ?‘ê·¼(?„í—˜)?¼ë¡œ ê°„ì£¼
                return PathSafetyResult.Outside;
            }
            catch { return PathSafetyResult.Outside; }
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// ???ˆì „??ê²°ê³¼ ì¤????„í—˜??ìµœì†Œ???ˆì „?±ì„ ê°€ì§? ê²°ê³¼ë¥?? íƒ?©ë‹ˆ??
        /// </summary>
        private PathSafetyResult GetMinSafety(PathSafetyResult current, PathSafetyResult @new)
        {
            // ?„í—˜???œìœ„: Outside (ìµœê³  ?„í—˜) > Workspace > SafeSystem > NotApplicable

            if (current == PathSafetyResult.Outside || @new == PathSafetyResult.Outside) return PathSafetyResult.Outside;
            if (current == PathSafetyResult.Workspace || @new == PathSafetyResult.Workspace) return PathSafetyResult.Workspace;
            if (current == PathSafetyResult.SafeSystem || @new == PathSafetyResult.SafeSystem) return PathSafetyResult.SafeSystem;
            return PathSafetyResult.NotApplicable;
        }

        private static string ResolveFinalPath(string fullPath)
        {
            try
            {
                string? current = fullPath;
                var missingSegments = new Stack<string>();

                // 1. ì¡´ì¬?˜ëŠ” ë¶€ëª?ê²½ë¡œë¥?ì°¾ì„ ?Œê¹Œì§€ ê±°ìŠ¬???¬ë¼ê°?
                while (!string.IsNullOrEmpty(current) && !File.Exists(current) && !Directory.Exists(current))
                {
                    string? segment = Path.GetFileName(current);
                    if (!string.IsNullOrEmpty(segment)) missingSegments.Push(segment);
                    current = Path.GetDirectoryName(current);
                }

                if (string.IsNullOrEmpty(current)) return fullPath;

                // 2. ì¡´ì¬?˜ëŠ” ë¶€ë¶„ì˜ ?¬ë³¼ë¦?ë§í¬ ì²´ì¸???´ê²°
                string resolved = ResolveSymlinkChain(current);

                // 3. ?˜ë¼?ˆë˜ ?˜ìœ„ ê²½ë¡œë¥??¤ì‹œ ê²°í•©
                while (missingSegments.Count > 0)
                {
                    resolved = Path.Combine(resolved, missingSegments.Pop());
                }

                return Path.GetFullPath(resolved);
            }
            catch
            {
                return fullPath;
            }
        }

        private static string ResolveSymlinkChain(string path)
        {
            string current = Path.GetFullPath(path);
            HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
            int depth = 0;
            const int maxDepth = 10;

            while (depth < maxDepth)
            {
                if (!visited.Add(current))
                    throw new InvalidOperationException($"Circular symlink detected at: {current}");

                FileSystemInfo info = File.Exists(current) ? new FileInfo(current) : new DirectoryInfo(current);

                // immediate targetë§?ê°€?¸ì˜´ (false)
                var target = info.ResolveLinkTarget(false);

                if (target == null) break;

                // ?ë? ê²½ë¡œ??ê²½ìš° ë¶€ëª?ê²½ë¡œ?€ ê²°í•©
                string targetPath = target.FullName;
                if (!Path.IsPathRooted(targetPath))
                {
                    string? parent = Path.GetDirectoryName(current);
                    targetPath = parent != null ? Path.Combine(parent, targetPath) : targetPath;
                }

                current = Path.GetFullPath(targetPath);
                depth++;
            }

            if (depth >= maxDepth)
                throw new InvalidOperationException($"Symlink chain too deep (max {maxDepth})");

            return current;
        }
    }
}
