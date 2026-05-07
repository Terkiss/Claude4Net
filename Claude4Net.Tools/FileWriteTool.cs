using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Tools
{
    /// <summary>
    /// FileWriteTool 실행을 위한 입력 매개변수 클래스입니다.
    /// </summary>
    public class FileWriteInput
    {
        /// <summary>
        /// 쓸 파일의 경로입니다.
        /// </summary>
        public string file_path { get; set; } = string.Empty;
        
        /// <summary>
        /// 파일에 작성할 내용입니다.
        /// </summary>
        public string content { get; set; } = string.Empty;
        
        /// <summary>
        /// LLM이 'file_path' 대신 'path'를 생성할 경우를 위한 대체 속성입니다.
        /// </summary>
        public string path { get => file_path; set => file_path = value; }
    }

    /// <summary>
    /// 파일에 내용을 작성하는 도구입니다. 기존 파일이 있으면 덮어씁니다.
    /// </summary>
    public class FileWriteTool : ITool, IPreviewableTool
    {
        public string Name => "FileWriteTool";
        public string Description => "Write content to a file.";
        public List<string>? Aliases => new() { "write" };
        public object? InputSchema => new { 
            type = "object",
             properties = new { 
                file_path = new { type = "string" }, 
                content = new { type = "string" } 
            }, 
            required = new[] { "file_path", "content" } };

        public async Task<FileDiffPreview?> GetPreviewAsync(string arguments)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<FileWriteInput>(arguments, options);
            if (input == null) return null;

            string? oldContent = null;
            var changeType = FileChangeType.Create;

            if (File.Exists(input.file_path))
            {
                oldContent = await File.ReadAllTextAsync(input.file_path);
                changeType = FileChangeType.Update;
            }

            return DiffService.CreatePreview(oldContent, input.content, input.file_path, changeType);
        }

        /// <summary>
        /// 내용을 지정된 경로의 파일에 비동기적으로 씁니다.
        /// </summary>
        /// <param name="arguments">JSON 형식의 쓰기 매개변수</param>
        /// <param name="context">실행 컨텍스트</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>쓰기 작업 결과 상태</returns>
        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<FileWriteInput>(arguments, options) 
                        ?? throw new ArgumentException("Invalid arguments");

            // [자동 생성] 대상 디렉토리가 존재하지 않을 경우 자동으로 생성합니다.
            string? dir = Path.GetDirectoryName(input.file_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) 
            {
                Directory.CreateDirectory(dir);
            }

            // 파일 쓰기 수행
            await File.WriteAllTextAsync(input.file_path, input.content, ct);
            
            return new { filePath = input.file_path, status = "Success" };
        }
    }
}
