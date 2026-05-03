using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 특정 플러그인에 대한 실행 지침 및 리소스를 담고 있는 매니페스트 클래스입니다.
    /// </summary>
    public class SkillResourceManifest
    {
        /// <summary>
        /// 리소스가 연결된 플러그인의 이름입니다.
        /// </summary>
        public string PluginName { get; set; } = string.Empty;

        /// <summary>
        /// 작업 전 확인해야 할 체크리스트 내용입니다 (checklist.md).
        /// </summary>
        public string? Checklist { get; set; }

        /// <summary>
        /// 에러 발생 시 대응 절차를 담은 플레이북 내용입니다 (error-playbook.md).
        /// </summary>
        public string? ErrorPlaybook { get; set; }

        /// <summary>
        /// 도구 사용 사례 및 예시를 담은 내용입니다 (examples.md).
        /// </summary>
        public string? Examples { get; set; }

        /// <summary>
        /// 구체적인 실행 규약이나 단계별 절차를 담은 내용입니다 (execution-protocol.md).
        /// </summary>
        public string? ExecutionProtocol { get; set; }

        /// <summary>
        /// 마지막으로 로드된 시각입니다.
        /// </summary>
        public DateTime LastLoaded { get; set; }

        /// <summary>
        /// 각 파일의 경로별 마지막 수정 시각을 기록하여 캐시 유효성 검사에 사용합니다.
        /// </summary>
        public Dictionary<string, DateTime> FileTimestamps { get; set; } = new();

        /// <summary>
        /// 모든 리소스 항목이 비어 있는지 여부를 반환합니다.
        /// </summary>
        public bool IsEmpty => 
            string.IsNullOrEmpty(Checklist) && 
            string.IsNullOrEmpty(ErrorPlaybook) && 
            string.IsNullOrEmpty(Examples) && 
            string.IsNullOrEmpty(ExecutionProtocol);
    }
}
