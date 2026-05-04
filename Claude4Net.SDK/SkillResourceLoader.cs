using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 플러그인별 스킬 리소스(체크리스트, 에러 플레이북 등)를 로드하고 관리하는 로더입니다.
    /// </summary>
    public class SkillResourceLoader
    {
        private static readonly ConcurrentDictionary<string, SkillResourceManifest> _cache = new();
        private readonly string _baseResourcesDir;

        /// <summary>
        /// SkillResourceLoader의 새 인스턴스를 초기화합니다.
        /// </summary>
        /// <param name="baseDir">리소스가 저장된 기본 디렉토리 경로. null일 경우 시스템 기본 경로 하위의 .resources 폴더를 사용합니다.</param>
        public SkillResourceLoader(string? baseDir = null)
        {
            _baseResourcesDir = baseDir ?? Path.Combine(AppState.SystemBaseDir, ".resources");
        }

        /// <summary>
        /// 특정 플러그인에 대한 리소스 매니페스트를 로드합니다. 캐싱된 정보가 있고 유효하다면 캐시를 반환합니다.
        /// </summary>
        /// <param name="pluginName">플러그인 이름</param>
        /// <returns>로드된 스킬 리소스 매니페스트</returns>
        public SkillResourceManifest LoadForPlugin(string pluginName)
        {
            string pluginDir = Path.Combine(_baseResourcesDir, pluginName);
            
            if (_cache.TryGetValue(pluginName, out var cached))
            {
                if (!IsCacheStale(pluginDir, cached))
                {
                    return cached;
                }
            }

            var manifest = new SkillResourceManifest { PluginName = pluginName };
            
            if (Directory.Exists(pluginDir))
            {
                manifest.Checklist = TryReadFile(Path.Combine(pluginDir, "checklist.md"), manifest);
                manifest.ErrorPlaybook = TryReadFile(Path.Combine(pluginDir, "error-playbook.md"), manifest);
                manifest.Examples = TryReadFile(Path.Combine(pluginDir, "examples.md"), manifest);
                manifest.ExecutionProtocol = TryReadFile(Path.Combine(pluginDir, "execution-protocol.md"), manifest);
            }

            manifest.LastLoaded = DateTime.Now;
            _cache[pluginName] = manifest;
            return manifest;
        }

        /// <summary>
        /// 디렉토리 내 파일들의 변경 여부를 확인하여 캐시가 만료되었는지 판단합니다.
        /// </summary>
        private bool IsCacheStale(string pluginDir, SkillResourceManifest cached)
        {
            if (!Directory.Exists(pluginDir)) return !cached.IsEmpty;

            foreach (var kvp in cached.FileTimestamps)
            {
                if (File.Exists(kvp.Key))
                {
                    if (File.GetLastWriteTime(kvp.Key) > kvp.Value) return true;
                }
                else
                {
                    // 파일이 삭제된 경우
                    return true;
                }
            }

            // 새로운 파일이 나타났는지 확인
            string[] expectedFiles = { "checklist.md", "error-playbook.md", "examples.md", "execution-protocol.md" };
            foreach (var file in expectedFiles)
            {
                string fullPath = Path.Combine(pluginDir, file);
                if (File.Exists(fullPath) && !cached.FileTimestamps.ContainsKey(fullPath)) return true;
            }

            return false;
        }

        /// <summary>
        /// 파일을 안전하게 읽고, 성공할 경우 매니페스트에 타임스탬프를 기록합니다.
        /// </summary>
        private string? TryReadFile(string path, SkillResourceManifest manifest)
        {
            if (File.Exists(path))
            {
                try
                {
                    manifest.FileTimestamps[path] = File.GetLastWriteTime(path);
                    return File.ReadAllText(path);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>
        /// 메모리에 캐싱된 모든 리소스를 비웁니다.
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
        }
    }
}
