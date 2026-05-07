using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 파일 변경 유형을 정의합니다.
    /// </summary>
    public enum FileChangeType
    {
        Create,
        Update,
        Delete,
        Rename
    }

    /// <summary>
    /// 파일 변경에 대한 프리뷰 및 Diff 정보를 담는 모델입니다.
    /// </summary>
    public class FileDiffPreview
    {
        /// <summary> 대상 파일 경로 </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary> 변경 유형 </summary>
        public FileChangeType ChangeType { get; set; }

        /// <summary> Diff 내용 (Unified Diff 포맷 등) </summary>
        public string DiffContent { get; set; } = string.Empty;

        /// <summary> 변경 전 전체 내용 (선택 사항) </summary>
        public string? OldContent { get; set; }

        /// <summary> 변경 후 전체 내용 (선택 사항) </summary>
        public string? NewContent { get; set; }

        /// <summary> 바이너리 파일 여부 </summary>
        public bool IsBinary { get; set; }
    }
}
