using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Tools
{
    /// <summary>
    /// FileEditTool 실행을 위한 입력 매개변수 클래스입니다.
    /// </summary>
    public class FileEditInput
    {
        /// <summary>
        /// 수정할 파일의 경로입니다.
        /// </summary>
        public string file_path { get; set; } = string.Empty;
        
        /// <summary>
        /// 교체 대상이 될 기존 문자열입니다.
        /// </summary>
        public string old_string { get; set; } = string.Empty;
        
        /// <summary>
        /// 새로 삽입할 문자열입니다.
        /// </summary>
        public string new_string { get; set; } = string.Empty;
        
        /// <summary>
        /// LLM이 'file_path' 대신 'path'를 생성할 경우를 위한 대체 속성입니다.
        /// </summary>
        public string path { get => file_path; set => file_path = value; }
    }

    /// <summary>
    /// 파일 내의 특정 문자열을 찾아 다른 문자열로 교체하는 도구입니다.
    /// </summary>
    public class FileEditTool : ITool, IPreviewableTool
    {
        public string Name => "FileEditTool";
        public string Description => "Edit a file.";
        public List<string>? Aliases => new() { "edit" };
        public object? InputSchema => new { type = "object", properties = new { file_path = new { type = "string" }, old_string = new { type = "string" }, new_string = new { type = "string" } }, required = new[] { "file_path", "old_string", "new_string" } };

        public async Task<FileDiffPreview?> GetPreviewAsync(string arguments)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<FileEditInput>(arguments, options);
            if (input == null) return null;

            if (!File.Exists(input.file_path)) return null;

            string content = await File.ReadAllTextAsync(input.file_path);
            if (!content.Contains(input.old_string)) return null;

            string updated = content.Replace(input.old_string, input.new_string);
            return DiffService.CreatePreview(content, updated, input.file_path, FileChangeType.Update);
        }

        /// <summary>
        /// 파일의 내용을 비동기적으로 읽어 문자열 치환을 수행하고 저장합니다.
        /// </summary>
        /// <param name="arguments">JSON 형식의 수정 매개변수</param>
        /// <param name="context">실행 컨텍스트</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>수정 결과 상태</returns>
        /// <exception cref="FileNotFoundException">파일이 존재하지 않을 경우 발생</exception>
        /// <exception cref="Exception">대상 문자열(old_string)을 찾을 수 없을 경우 발생</exception>
        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<FileEditInput>(arguments, options) 
                        ?? throw new ArgumentException("Invalid arguments");

            // [안전장치] 파일 존재 여부 확인
            if (!File.Exists(input.file_path)) throw new FileNotFoundException($"File not found: {input.file_path}");

            // 파일 전체 내용을 읽어옴
            string content = await File.ReadAllTextAsync(input.file_path, ct);
            
            // [무결성 검사] 교체하려는 문자열이 실제로 존재하는지 확인
            if (!content.Contains(input.old_string)) throw new Exception("String not found. The exact original text must be provided for replacement.");

            // 문자열 치환 수행
            string updated = content.Replace(input.old_string, input.new_string);
            
            // 변경된 내용을 파일에 다시 씀
            await File.WriteAllTextAsync(input.file_path, updated, ct);

            return new { filePath = input.file_path, status = "Success" };
        }
    }
}
