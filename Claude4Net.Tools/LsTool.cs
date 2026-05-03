using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Tools
{
    /// <summary>
    /// LsTool 실행을 위한 입력 매개변수 클래스입니다.
    /// </summary>
    public class LsInput 
    { 
        /// <summary>
        /// 목록을 조회할 디렉토리 경로입니다.
        /// </summary>
        public string path { get; set; } = string.Empty; 
    }

    /// <summary>
    /// 지정된 경로의 파일 및 디렉토리 목록을 나열하는 도구입니다.
    /// </summary>
    public class LsTool : ITool
    {
        public string Name => "LsTool";
        public string Description => "List files and directories in a given path.";
        public object? InputSchema => new { type = "object", properties = new { path = new { type = "string", description = "Directory path to list" } }, required = new[] { "path" } };
        public bool IsConcurrencySafe => true;

        /// <summary>
        /// 디렉토리 목록을 비동기적으로 조회합니다.
        /// </summary>
        /// <param name="arguments">JSON 형식의 경로 매개변수</param>
        /// <param name="context">실행 컨텍스트</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>조회된 경로와 파일/디렉토리 목록</returns>
        /// <exception cref="DirectoryNotFoundException">디렉토리가 존재하지 않을 경우 발생</exception>
        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<LsInput>(arguments, options) ?? new LsInput();
            
            // [기본값 설정] 경로가 제공되지 않으면 현재 디렉토리를 사용합니다.
            string targetPath = string.IsNullOrEmpty(input.path) ? Environment.CurrentDirectory : input.path;
            
            // [안전장치] 디렉토리 존재 여부 확인
            if (!Directory.Exists(targetPath)) throw new DirectoryNotFoundException($"Directory not found: {targetPath}");

            // 파일 시스템 항목 조회 및 포맷팅 (디렉토리는 끝에 '/'를 붙여 구분)
            var entries = Directory.GetFileSystemEntries(targetPath)
                .Select(e => Path.GetFileName(e) + (Directory.Exists(e) ? "/" : ""))
                .ToList();

            return new { path = targetPath, entries = entries };
        }
    }
}
