using System;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 애플리케이션의 런타임 상태 및 설정을 추상화하는 인터페이스입니다.
    /// </summary>
    public interface IAppState
    {
        /// <summary> 현재 세션의 고유 ID </summary>
        string SessionId { get; set; }

        /// <summary> 사용자의 현재 작업 디렉토리 </summary>
        string? CurrentCwd { get; set; }

        /// <summary> 현재 권한 모드 </summary>
        PermissionMode CurrentPermissionMode { get; set; }

        /// <summary> 활성화된 LLM 제공자 이름 </summary>
        string ActiveProvider { get; set; }

        /// <summary> 현재 사용 중인 모델 이름 </summary>
        string ActiveModel { get; set; }

        /// <summary> Discord 승인자 목록을 로드합니다. </summary>
        void LoadDiscordApprovers();
    }
}
