using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// 경로 안전성 평가 결과 열거형입니다.
    /// </summary>
    public enum PathSafetyResult
    {
        /// <summary> 경로 평가가 해당되지 않음 (일반 텍스트 등) </summary>
        NotApplicable,
        /// <summary> 시스템 보호 구역 (db, Skills 폴더 등) 내의 안전한 접근 </summary>
        SafeSystem,
        /// <summary> 설정된 작업 공간(Workspace) 내의 정상적인 접근 </summary>
        Workspace,
        /// <summary> 허용되지 않은 외부 경로 또는 금지된 구역 접근 </summary>
        Outside
    }

    /// <summary>
    /// 도구에 입력된 경로의 안전성을 평가하는 엔진입니다.
    /// 에이전트가 허용된 샌드박스(Workspace) 외부의 파일에 접근하거나 시스템 파일을 손상시키는 것을 방지합니다.
    /// </summary>
    public class PathSafetyEvaluator
    {
        /// <summary>
        /// 도구의 입력 객체 전체를 재귀적으로 탐색하여 포함된 모든 경로의 안전성을 평가합니다.
        /// </summary>
        /// <param name="input">도구 실행에 사용될 입력 데이터</param>
        /// <returns>탐색된 경로 중 가장 위험한 수준의 결과</returns>
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
        /// JSON 요소를 재귀적으로 분석하여 경로 또는 명령어 필드를 식별하고 검사합니다.
        /// </summary>
        private PathSafetyResult CheckElementSafety(JsonElement element)
        {
            PathSafetyResult minSafety = PathSafetyResult.NotApplicable;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    PathSafetyResult s;
                    // 명령어 실행(Bash 등)이나 SQL 쿼리 내부의 경로도 가로채어 검사
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
        /// 쉘 명령어 문자열을 토큰화하여 각 인자가 안전한 경로인지 검사합니다.
        /// </summary>
        private PathSafetyResult CheckCommandSafety(string? cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return PathSafetyResult.NotApplicable;

            // 공백 및 쉘 제어 문자 단위로 분리
            string[] tokens = cmd.Split(new[] { ' ', '\t', '|', '>', '<', '&', ';' }, StringSplitOptions.RemoveEmptyEntries);
            PathSafetyResult maxRisk = PathSafetyResult.NotApplicable;

            foreach (var token in tokens)
            {
                string t = token.Trim('\'', '\"');
                
                bool isWindowsFlag = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

                // Windows 스타일의 CLI 플래그(/f, /y 등)는 경로 검사에서 제외하는 휴리스틱 적용 (Windows 환경에서만)
                if (isWindowsFlag && t.StartsWith("/") && t.Length <= 10 && !t.Contains("\\") && t.LastIndexOf('/') == 0)
                    continue;

                PathSafetyResult s = EvaluateSinglePathSafety(t);
                maxRisk = GetMinSafety(maxRisk, s);
                if (maxRisk == PathSafetyResult.Outside) return PathSafetyResult.Outside;
            }
            return maxRisk == PathSafetyResult.NotApplicable ? PathSafetyResult.Workspace : maxRisk; 
        }

        /// <summary>
        /// 단일 문자열이 나타내는 경로가 샌드박스 정책을 준수하는지 평가합니다.
        /// </summary>
        /// <param name="targetPath">검사할 대상 경로 문자열</param>
        public PathSafetyResult EvaluateSinglePathSafety(string? targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return PathSafetyResult.NotApplicable;

            bool isWindowsFlag = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            // CLI 플래그 여부 재확인 (휴리스틱, Windows 환경에서만)
            if (isWindowsFlag && targetPath.StartsWith("/"))
            {
                bool hasInternalSlash = targetPath.IndexOf('/', 1) > 0;
                bool isShortAlphanumeric = targetPath.Length <= 10 && targetPath.Substring(1).All(char.IsLetterOrDigit);
                
                if (!hasInternalSlash && isShortAlphanumeric) return PathSafetyResult.NotApplicable;
            }

            try
            {
                // URI 형식 처리
                if (targetPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    targetPath = new Uri(targetPath).LocalPath;
                }

                // 단순 파일명만 있거나 경로 구분자가 없는 경우 상대 경로로 간주하고 허용 여부 유보
                if (!targetPath.Contains(Path.DirectorySeparatorChar) && 
                    !targetPath.Contains(Path.AltDirectorySeparatorChar) && 
                    !targetPath.Contains("..")) return PathSafetyResult.NotApplicable;

                // 절대 경로로 변환하여 상대 경로 우회(Traversal) 방지
                string fullPath = Path.GetFullPath(targetPath);
                
                bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                var comparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

                // 1. 시스템 내부 보호 구역(db, Skills) 체크
                string sysPath = Path.GetFullPath(AppState.SystemBaseDir);
                string normSys = sysPath.EndsWith(Path.DirectorySeparatorChar) ? sysPath : sysPath + Path.DirectorySeparatorChar;

                if (fullPath.StartsWith(normSys, comparison) || fullPath.Equals(sysPath, comparison))
                {
                    // 시스템 폴더 내에서도 오직 db와 Skills 폴더만 접근 허용
                    if (fullPath.Contains($"{Path.DirectorySeparatorChar}db{Path.DirectorySeparatorChar}") || 
                        fullPath.Contains($"{Path.DirectorySeparatorChar}Skills{Path.DirectorySeparatorChar}") ||
                        fullPath.EndsWith($"{Path.DirectorySeparatorChar}db") ||
                        fullPath.EndsWith($"{Path.DirectorySeparatorChar}Skills"))
                    {
                        return PathSafetyResult.SafeSystem;
                    }
                    return PathSafetyResult.Outside; 
                }

                // 2. 사용자 작업 공간(Workspace) 체크
                if (!string.IsNullOrEmpty(AppState.CurrentCwd))
                {
                    string wsPath = Path.GetFullPath(AppState.CurrentCwd);
                    string normWs = wsPath.EndsWith(Path.DirectorySeparatorChar) ? wsPath : wsPath + Path.DirectorySeparatorChar;
                    
                    if (fullPath.StartsWith(normWs, comparison) || fullPath.Equals(wsPath, comparison))
                    {
                        // YOLO 모드가 아닐 경우 .git이나 .gemini 같은 민감한 내부 폴더 접근 차단
                        if (AppState.CurrentPermissionMode != PermissionMode.Yolo)
                        {
                            if (fullPath.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") || 
                                fullPath.Contains($"{Path.DirectorySeparatorChar}.gemini{Path.DirectorySeparatorChar}"))
                                return PathSafetyResult.Outside;
                        }
                        return PathSafetyResult.Workspace;
                    }
                }

                // 어디에도 속하지 않으면 외부 접근(위험)으로 간주
                return PathSafetyResult.Outside;
            }
            catch { return PathSafetyResult.Outside; } 
        }

        /// <summary>
        /// 두 안전성 결과 중 더 위험한(최소의 안전성을 가진) 결과를 선택합니다.
        /// </summary>
        private PathSafetyResult GetMinSafety(PathSafetyResult current, PathSafetyResult @new)
        {
            // 위험도 순위: Outside (최고 위험) > Workspace > SafeSystem > NotApplicable
            
            if (current == PathSafetyResult.Outside || @new == PathSafetyResult.Outside) return PathSafetyResult.Outside;
            if (current == PathSafetyResult.Workspace || @new == PathSafetyResult.Workspace) return PathSafetyResult.Workspace;
            if (current == PathSafetyResult.SafeSystem || @new == PathSafetyResult.SafeSystem) return PathSafetyResult.SafeSystem;
            return PathSafetyResult.NotApplicable;
        }
    }
}
