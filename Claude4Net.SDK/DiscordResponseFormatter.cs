using System;
using System.Text;

namespace Claude4Net.SDK
{
    /// <summary>
    /// Discord 메시지 응답 형식을 생성하는 유틸리티 클래스입니다.
    /// </summary>
    public static class DiscordResponseFormatter
    {
        /// <summary>
        /// 작업 시작 메시지를 포맷팅합니다.
        /// </summary>
        /// <param name="user">요청 사용자 이름</param>
        /// <param name="text">요청 본문</param>
        /// <returns>포맷팅된 문자열</returns>
        public static string FormatStart(string user, string text)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"🚀 **Task Started** for @{user}");
            sb.AppendLine($"> {Truncate(text, 100)}"); // 요청 내용을 일부 잘라서 표시
            return sb.ToString();
        }

        /// <summary>
        /// 작업 성공 메시지를 포맷팅합니다.
        /// </summary>
        /// <param name="result">실행 결과 텍스트</param>
        /// <param name="duration">소요 시간 (선택 사항)</param>
        /// <returns>포맷팅된 문자열</returns>
        public static string FormatSuccess(string result, TimeSpan? duration = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("✅ **Task Completed**");
            sb.AppendLine("```");
            sb.AppendLine(Truncate(result, 1500)); // 결과가 너무 길면 Discord 제한에 맞춰 자름
            sb.AppendLine("```");
            if (duration.HasValue)
            {
                sb.AppendLine($"⏱️ **Duration**: {duration.Value.TotalSeconds:F1}s");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 작업 실패 메시지를 포맷팅합니다.
        /// </summary>
        /// <param name="error">오류 메시지</param>
        /// <returns>포맷팅된 문자열</returns>
        public static string FormatError(string error)
        {
            return $"❌ **Task Failed**: {error}";
        }

        /// <summary>
        /// 문자열이 일정 길이를 넘으면 잘라내고 생략 부호(...)를 붙입니다.
        /// </summary>
        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
        }
    }
}
