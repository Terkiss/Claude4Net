using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Tools
{
    /// <summary>
    /// FileReadTool 실행을 위한 입력 매개변수 클래스입니다.
    /// </summary>
    public class FileReadInput
    {
        /// <summary>
        /// 읽을 파일의 경로입니다.
        /// </summary>
        public string file_path { get; set; } = string.Empty;
        
        /// <summary>
        /// (선택 사항) 시작할 행 번호(1-based)입니다.
        /// </summary>
        public int? offset { get; set; }
        
        /// <summary>
        /// (선택 사항) 읽을 최대 행 수입니다.
        /// </summary>
        public int? limit { get; set; }

        /// <summary>
        /// LLM이 'file_path' 대신 'path'를 생성할 경우를 위한 대체 속성입니다.
        /// </summary>
        public string path { get => file_path; set => file_path = value; }
    }

    /// <summary>
    /// 파일의 내용을 읽어오는 도구입니다. 오프셋과 리미트를 통해 대용량 파일의 일부만 읽을 수 있습니다.
    /// </summary>
    public class FileReadTool : ITool
    {
        public string Name => "FileReadTool";
        public string Description => "Read the content of a file.";
        public List<string>? Aliases => new() { "read" };
        public object? InputSchema => new { type = "object", properties = new { file_path = new { type = "string", description = "Path to read" } }, required = new[] { "file_path" } };
        public bool IsConcurrencySafe => true;

        /// <summary>
        /// 파일 내용을 비동기적으로 읽어 반환합니다.
        /// </summary>
        /// <param name="arguments">JSON 형식의 읽기 매개변수</param>
        /// <param name="context">실행 컨텍스트</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>파일 내용 및 메타데이터</returns>
        /// <exception cref="FileNotFoundException">파일이 존재하지 않을 경우 발생</exception>
        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<FileReadInput>(arguments, options) 
                        ?? throw new ArgumentException("Invalid arguments");

            // [안전장치] 파일 존재 여부 확인
            if (!File.Exists(input.file_path)) throw new FileNotFoundException($"File not found: {input.file_path}");

            // 모든 행을 읽어옴 (메모리 사용 최적화가 필요할 경우 스트림 방식으로 전환 고려 가능)
            var allLines = await File.ReadAllLinesAsync(input.file_path, ct);
            
            // 오프셋 및 리미트 계산 (기본값: 전체 읽기)
            int startLine = input.offset ?? 1;
            int lineCount = input.limit ?? (allLines.Length - startLine + 1);

            // [데이터 가공] 지정된 범위의 행만 선택
            var selectedLines = allLines.Skip(Math.Max(0, startLine - 1)).Take(Math.Max(0, lineCount)).ToList();
            
            return new { 
                filePath = input.file_path, 
                content = string.Join("\n", selectedLines), 
                totalLines = allLines.Length 
            };
        }
    }
}
