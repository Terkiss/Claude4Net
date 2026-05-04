using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// LSP(Language Server Protocol)에서의 위치 정보를 정의합니다.
    /// </summary>
    public class LspPosition
    {
        /// <summary> 0부터 시작하는 줄 번호 </summary>
        public int Line { get; set; }
        /// <summary> 0부터 시작하는 해당 줄의 문자 위치 </summary>
        public int Character { get; set; }
    }

    /// <summary>
    /// LSP에서의 범위(시작~끝) 정보를 정의합니다.
    /// </summary>
    public class LspRange
    {
        /// <summary> 시작 위치 </summary>
        public LspPosition Start { get; set; } = new();
        /// <summary> 종료 위치 </summary>
        public LspPosition End { get; set; } = new();
    }

    /// <summary>
    /// 파일 내 특정 범위를 나타내는 위치 정보입니다.
    /// </summary>
    public class LspLocation
    {
        /// <summary> 파일 URI </summary>
        public string Uri { get; set; } = string.Empty;
        /// <summary> 해당 파일 내 범위 </summary>
        public LspRange Range { get; set; } = new();
    }

    /// <summary>
    /// 정의 이동 등에서 사용되는 위치 링크 정보입니다.
    /// </summary>
    public class LspLocationLink
    {
        /// <summary> 링크가 활성화된 원본 범위 </summary>
        public LspRange? OriginSelectionRange { get; set; }
        /// <summary> 대상 파일 URI </summary>
        public string TargetUri { get; set; } = string.Empty;
        /// <summary> 대상 파일 내 전체 범위 </summary>
        public LspRange TargetRange { get; set; } = new();
        /// <summary> 대상 파일 내 실제 선택될 범위 </summary>
        public LspRange TargetSelectionRange { get; set; } = new();
    }

    /// <summary>
    /// 심볼(클래스, 메서드 등) 정보를 담는 모델입니다.
    /// </summary>
    public class LspSymbolInformation
    {
        /// <summary> 심볼 이름 </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> 심볼 종류 (숫자 코드) </summary>
        public int Kind { get; set; }
        /// <summary> 위치 정보 </summary>
        public LspLocation Location { get; set; } = new();
        /// <summary> 상위 컨테이너 이름 </summary>
        public string? ContainerName { get; set; }
    }

    /// <summary>
    /// 계층 구조를 가진 문서 심볼 정보를 담는 모델입니다.
    /// </summary>
    public class LspDocumentSymbol
    {
        /// <summary> 심볼 이름 </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> 상세 설명 </summary>
        public string? Detail { get; set; }
        /// <summary> 심볼 종류 </summary>
        public int Kind { get; set; }
        /// <summary> 전체 범위 </summary>
        public LspRange Range { get; set; } = new();
        /// <summary> 선택 범위 </summary>
        public LspRange SelectionRange { get; set; } = new();
        /// <summary> 하위 심볼 목록 </summary>
        public List<LspDocumentSymbol>? Children { get; set; }
    }

    /// <summary>
    /// 마우스 오버 시 표시되는 호버(Hover) 정보를 담는 모델입니다.
    /// </summary>
    public class LspHover
    {
        /// <summary> 표시할 내용 (문자열 또는 MarkupContent) </summary>
        public object Contents { get; set; } = new(); 
        /// <summary> 해당 호버 정보가 적용되는 범위 </summary>
        public LspRange? Range { get; set; }
    }
}
