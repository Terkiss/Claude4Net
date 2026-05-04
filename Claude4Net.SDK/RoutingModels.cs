using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// LLM 제공자의 상태를 정의하는 열거형입니다.
    /// </summary>
    public enum ProviderHealthStatus
    {
        /// <summary> 정상 </summary>
        Healthy,
        /// <summary> 성능 저하 </summary>
        Degraded,
        /// <summary> 비정상 </summary>
        Unhealthy,
        /// <summary> 서킷 브레이커 작동 중 (차단됨) </summary>
        CircuitBroken,
        /// <summary> 서킷 브레이커 반개방 (복구 시도 중) </summary>
        CircuitBreakerHalfOpen
    }

    /// <summary>
    /// 제공자별 성능 및 상태 메트릭을 관리하는 클래스입니다.
    /// </summary>
    public class ProviderMetric
    {
        /// <summary> 제공자 이름 </summary>
        public string ProviderName { get; set; } = string.Empty;
        /// <summary> 지연 시간의 지수 이동 평균 (ms) </summary>
        public double LatencyEma { get; set; } 
        /// <summary> 현재 상태 </summary>
        public ProviderHealthStatus Status { get; set; } = ProviderHealthStatus.Healthy;
        /// <summary> 누적 오류 횟수 </summary>
        public int ErrorCount { get; set; }
        /// <summary> 누적 성공 횟수 </summary>
        public int SuccessCount { get; set; }
        /// <summary> 마지막 업데이트 시간 </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        /// <summary> 비용 점수 (0.0: 저렴, 1.0: 비쌈) </summary>
        public double CostScore { get; set; } 
        /// <summary> 현재 세션에서 누적된 예상 비용 </summary>
        public double AccumulatedCost { get; set; } 
        /// <summary> 서킷 브레이커 리셋 예정 시간 (지수 백오프용) </summary>
        public DateTime? CircuitBreakerResetTime { get; set; } 
    }

    /// <summary>
    /// 라우팅 시 고려할 의도(Intent)를 정의합니다.
    /// </summary>
    public enum RoutingIntent
    {
        /// <summary> 시스템 자동 선택 </summary>
        Auto,
        /// <summary> 복잡한 추론이 필요한 대형 모델 선호 (예: Claude 3.5 Sonnet, Gemini 1.5 Pro) </summary>
        LargeModel, 
        /// <summary> 빠르고 단순한 작업용 소형 모델 선호 (예: Gemini 1.5 Flash, Llama 3 8B) </summary>
        SmallModel, 
        /// <summary> 로컬 모델만 사용 (예: Ollama) </summary>
        LocalOnly,  
        /// <summary> 비용 효율성 우선 </summary>
        CostEffective
    }

    /// <summary>
    /// 라우팅 결정 결과 정보를 담는 모델입니다.
    /// </summary>
    public class RoutingDecision
    {
        /// <summary> 선택된 제공자 </summary>
        public string SelectedProvider { get; set; } = string.Empty;
        /// <summary> 선택된 모델 </summary>
        public string SelectedModel { get; set; } = string.Empty;
        /// <summary> 선택 이유 </summary>
        public string Reason { get; set; } = string.Empty;
        /// <summary> 실패 시 시도할 폴백(Fallback) 제공자 목록 </summary>
        public List<string> FallbackChain { get; set; } = new();
    }

    /// <summary>
    /// 최적의 LLM 제공자를 결정하기 위한 스마트 라우터 인터페이스입니다.
    /// </summary>
    public interface ISmartRouter
    {
        /// <summary> 주어진 프롬프트와 의도에 따라 최적의 제공자를 선택합니다. </summary>
        RoutingDecision Route(string prompt, RoutingIntent intent = RoutingIntent.Auto);
        /// <summary> 제공자의 실행 결과를 메트릭에 반영합니다. </summary>
        void UpdateMetric(string provider, double latencyMs, bool isError);
        /// <summary> 현재 기록된 모든 메트릭 목록을 가져옵니다. </summary>
        IEnumerable<ProviderMetric> GetMetrics();
    }
}
