using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 파일 변경 사항에 대한 Diff를 생성하고 분석하는 서비스입니다.
    /// </summary>
    public class DiffService
    {
        /// <summary>
        /// 두 텍스트 간의 차이점을 Unified Diff 스타일로 생성합니다.
        /// </summary>
        public static string GenerateUnifiedDiff(string oldText, string newContent, string filePath)
        {
            var oldLines = oldText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var newLines = newContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var sb = new StringBuilder();
            sb.AppendLine($"--- {filePath} (original)");
            sb.AppendLine($"+++ {filePath} (proposed)");

            int i = 0, j = 0;
            while (i < oldLines.Length || j < newLines.Length)
            {
                if (i < oldLines.Length && j < newLines.Length && oldLines[i] == newLines[j])
                {
                    i++; j++;
                }
                else if (i < oldLines.Length && (j >= newLines.Length || !newLines.Skip(j).Contains(oldLines[i])))
                {
                    sb.AppendLine($"- {oldLines[i]}");
                    i++;
                }
                else if (j < newLines.Length)
                {
                    sb.AppendLine($"+ {newLines[j]}");
                    j++;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 파일의 변경 사항에 대한 프리뷰 객체를 생성합니다.
        /// </summary>
        public static FileDiffPreview CreatePreview(string? oldContent, string newContent, string filePath, FileChangeType type)
        {
            bool isBinary = IsBinary(newContent);
            string diff = string.Empty;

            if (!isBinary)
            {
                if (type == FileChangeType.Create)
                {
                    diff = $"+ [New File] {filePath}\n" + string.Join("\n", newContent.Split('\n').Select(l => $"+ {l}"));
                }
                else if (type == FileChangeType.Update && oldContent != null)
                {
                    diff = GenerateUnifiedDiff(oldContent, newContent, filePath);
                }
            }
            else
            {
                diff = "[Binary file changes not shown]";
            }

            return new FileDiffPreview
            {
                FilePath = filePath,
                ChangeType = type,
                DiffContent = diff,
                OldContent = oldContent,
                NewContent = newContent,
                IsBinary = isBinary
            };
        }

        private static bool IsBinary(string text)
        {
            return text.Take(1024).Any(ch => ch == '\0');
        }
    }
}
