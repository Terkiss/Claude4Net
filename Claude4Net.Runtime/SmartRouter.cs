using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// 스마트 라우팅 엔진으로, 지수 이동 평균(EMA) 및 비용/지연 시간 스코어링을 통해
    /// 최적의 LLM 프로바이더를 동적으로 선정합니다.
    /// 서킷 브레이커 패턴을 통해 장애 발생 시 안정적인 Fallback을 보장합니다.
    /// </summary>
    public class SmartRouter : ISmartRouter
    {
        private readonly ConcurrentDictionary<string, ProviderMetric> _metrics = new();
        
        /// <summary>
        /// EMA(Exponential Moving Average) 가중치 (0.3).
        /// 최근 측정값에 더 높은 비중을 두어 변화에 민감하게 반응하도록 설정함.
        /// </summary>
        private const double Alpha = 0.3; 
        
        /// <summary>
        /// 서킷 브레이커가 작동하기 위한 연속 에러 임계치 (5회).
        /// </summary>
        private const int CircuitBreakerThreshold = 5;
        
        /// <summary>
        /// 서킷 브레이커 오픈 후 재시도 대기 시간의 기본값.
        /// </summary>
        private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(30);

        /// <summary>
        /// SmartRouter의 새 인스턴스를 초기화하고 알려진 프로바이더의 메트릭을 기본값으로 설정합니다.
        /// </summary>
        public SmartRouter()
        {
            // 초기 비용 점수 설정 (0.0: 무료/로컬, 1.0: 고가용성 유료 서비스)
            InitializeProvider("claude", 0.8);
            InitializeProvider("gemini", 0.4);
            InitializeProvider("ollama", 0.1);
            InitializeProvider("gemini-cli", 0.0);
        }

        private void InitializeProvider(string name, double costScore)
        {
            _metrics[name] = new ProviderMetric
            {
                ProviderName = name,
                LatencyEma = 1000, // 초기 지연 시간 예상치 1s
                Status = ProviderHealthStatus.Healthy,
                CostScore = costScore,
                AccumulatedCost = 0,
                LastUpdated = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 현재 입력된 프롬프트와 의도를 분석하여 가장 적합한 LLM 프로바이더를 선택합니다.
        /// </summary>
        /// <param name="prompt">사용자 입력 텍스트</param>
        /// <param name="intent">요청의 의도 (Auto 시 자동 분석)</param>
        /// <returns>선택된 프로바이더와 모델 정보를 포함한 RoutingDecision</returns>
        public RoutingDecision Route(string prompt, RoutingIntent intent = RoutingIntent.Auto)
        {
            var now = DateTime.UtcNow;
            var candidates = _metrics.Values.ToList();

            // 1. 서킷 브레이커 상태 전이 확인: CircuitBroken -> HalfOpen
            // 대기 시간이 경과한 경우 재시험(Half-Open) 상태로 전환
            foreach (var m in candidates.Where(m => m.Status == ProviderHealthStatus.CircuitBroken))
            {
                if (m.CircuitBreakerResetTime.HasValue && now >= m.CircuitBreakerResetTime.Value)
                {
                    m.Status = ProviderHealthStatus.CircuitBreakerHalfOpen;
                }
            }

            // 2. 가용한 프로바이더 필터링 (장애 상태인 경우 제외)
            var healthyProviders = candidates
                .Where(m => m.Status != ProviderHealthStatus.CircuitBroken && m.Status != ProviderHealthStatus.Unhealthy)
                .ToList();

            // 모든 프로바이더가 가용 불능인 경우 로컬 Ollama를 최후의 수단으로 선택
            if (!healthyProviders.Any())
            {
                return new RoutingDecision
                {
                    SelectedProvider = "ollama",
                    SelectedModel = "llama3",
                    Reason = "All remote providers are unhealthy or circuit-broken. Falling back to local Ollama."
                };
            }

            // 3. 의도 분석 (Auto 인 경우 프롬프트 길이나 키워드로 자동 추정)
            var effectiveIntent = intent;
            if (effectiveIntent == RoutingIntent.Auto)
            {
                if (prompt.Length > 1000 || prompt.Contains("complex") || prompt.Contains("refactor"))
                    effectiveIntent = RoutingIntent.LargeModel;
                else if (prompt.Length < 100)
                    effectiveIntent = RoutingIntent.SmallModel;
            }

            // 4. 스코어링 로직 적용: 지연 시간, 비용, 누적 사용량, 의도 적합성 계산
            var scored = healthyProviders.Select(m => new
            {
                Metric = m,
                Score = CalculateScore(m, effectiveIntent, prompt, intent == RoutingIntent.Auto)
            }).OrderByDescending(x => x.Score).ToList();

            var top = scored.First();

            return new RoutingDecision
            {
                SelectedProvider = top.Metric.ProviderName,
                SelectedModel = DefaultModelFor(top.Metric.ProviderName, effectiveIntent),
                Reason = $"Selected {top.Metric.ProviderName} for {effectiveIntent} intent (Score: {top.Score:F2}, Latency: {top.Metric.LatencyEma:F0}ms, Health: {top.Metric.Status})",
                FallbackChain = scored.Skip(1).Select(x => x.Metric.ProviderName).ToList()
            };
        }

        /// <summary>
        /// 특정 프로바이더의 적합성 점수를 계산합니다.
        /// </summary>
        /// <remarks>
        /// 수식: Score = 100 - (지연시간 패널티) - (기본 비용 패널티) - (누적 사용량 패널티) + (의도 가중치)
        /// </remarks>
        private double CalculateScore(ProviderMetric m, RoutingIntent intent, string prompt, bool wasAuto)
        {
            double score = 100.0;

            // 패널티 1. 지연 시간 (EMA): 100ms 당 -1점 감점.
            // 로컬 프로바이더(Ollama 등)는 지연 시간 패널티에서 제외하여 로컬 환경 우선순위 부여.
            if (!IsLocalProvider(m.ProviderName))
            {
                score -= (m.LatencyEma / 100.0);
            }

            // 사용자가 명시적으로 선택한 프로바이더에 대해서는 압도적인 보너스 점수 부여
            if (AppState.IsProviderExplicitlySet && m.ProviderName == AppState.ActiveProvider)
            {
                score += 10000.0;
            }

            // 패널티 2. 기본 비용 가중치: 의도가 '비용 효율'인 경우 감점 폭을 키움.
            double costWeight = (intent == RoutingIntent.CostEffective) ? 50.0 : 10.0;
            score -= (m.CostScore * costWeight);

            // 패널티 3. 누적 사용량(Accumulated Cost): 부하 분산을 위해 최근 사용이 많은 프로바이더 감점.
            score -= (m.AccumulatedCost * 5.0);

            // 보너스 4. 로컬 모델 보호 및 의도 정렬 가중치
            if (IsLocalProvider(m.ProviderName))
            {
                // 자동 모드일 때 로컬 모델을 더 선호하도록 보너스 점수 부여
                score += wasAuto ? 2000.0 : 500.0;
            }

            switch (intent)
            {
                case RoutingIntent.LargeModel:
                    if (m.ProviderName == "claude") score += 1500.0; 
                    if (m.ProviderName == "gemini") score += 1000.0;
                    break;
                case RoutingIntent.SmallModel:
                    if (m.ProviderName == "gemini") score += 600.0;
                    if (m.ProviderName == "ollama") score += 20.0;
                    break;
                case RoutingIntent.LocalOnly:
                    if (IsLocalProvider(m.ProviderName)) score += 1000.0;
                    else score -= 2000.0;
                    break;
                case RoutingIntent.CostEffective:
                    if (IsLocalProvider(m.ProviderName)) score += 300.0;
                    break;
            }

            // 패널티 5. 건강 상태 패널티: 성능 저하나 Half-Open 상태일 때 감점.
            if (m.Status == ProviderHealthStatus.Degraded) score -= 40.0;
            if (m.Status == ProviderHealthStatus.CircuitBreakerHalfOpen) score -= 60.0;

            return score;
        }

        private bool IsLocalProvider(string name)
        {
            return name == "ollama" || name == "gemini-cli";
        }

        private string DefaultModelFor(string provider, RoutingIntent intent)
        {
            // 사용자가 명시적으로 프로바이더와 모델을 설정한 경우, 시스템의 자동 선택보다 우선함
            if (AppState.IsProviderExplicitlySet && provider.Equals(AppState.ActiveProvider, StringComparison.OrdinalIgnoreCase))
            {
                return AppState.ActiveModel;
            }

            return provider switch
            {
                "claude" => (intent == RoutingIntent.LargeModel) ? "claude-3-5-sonnet-20240620" : "claude-3-haiku-20240307",
                "gemini" => (intent == RoutingIntent.LargeModel) ? "gemini-1.5-pro" : "gemini-1.5-flash",
                "ollama" => "llama3",
                "gemini-cli" => AppState.ActiveModel ?? "gemini-3.1-pro",
                _ => AppState.ActiveModel ?? "gemini-3.1-pro"
            };
        }

        /// <summary>
        /// 프로바이더의 실행 결과를 바탕으로 메트릭을 업데이트합니다.
        /// EMA 방식으로 지연 시간을 계산하고, 에러 발생 시 서킷 브레이커 로직을 처리합니다.
        /// </summary>
        /// <param name="provider">프로바이더 이름</param>
        /// <param name="latencyMs">실제 소요된 지연 시간(ms)</param>
        /// <param name="isError">에러 발생 여부</param>
        public void UpdateMetric(string provider, double latencyMs, bool isError)
        {
            _metrics.AddOrUpdate(provider, 
                _ => new ProviderMetric { 
                    ProviderName = provider, 
                    LatencyEma = latencyMs, 
                    Status = isError ? ProviderHealthStatus.Degraded : ProviderHealthStatus.Healthy,
                    AccumulatedCost = isError ? 0 : (latencyMs / 1000.0),
                    LastUpdated = DateTime.UtcNow
                },
                (name, old) =>
                {
                    // EMA(지수 이동 평균) 수식 적용: NewValue * Alpha + OldValue * (1 - Alpha)
                    old.LatencyEma = (Alpha * latencyMs) + (1 - Alpha) * old.LatencyEma;
                    
                    if (isError)
                    {
                        old.ErrorCount++;
                        old.SuccessCount = 0;
                        // 연속 에러 임계치 도달 시 서킷 오픈
                        if (old.ErrorCount >= CircuitBreakerThreshold)
                        {
                            old.Status = ProviderHealthStatus.CircuitBroken;
                            // 지수 백오프(Exponential Backoff) 적용: Base * 2^(errorCount - threshold)
                            int backoffFactor = Math.Min(old.ErrorCount - CircuitBreakerThreshold, 6);
                            old.CircuitBreakerResetTime = DateTime.UtcNow.Add(BaseBackoff.Multiply(Math.Pow(2, backoffFactor)));
                        }
                        else
                        {
                            old.Status = ProviderHealthStatus.Degraded;
                        }
                    }
                    else
                    {
                        old.SuccessCount++;
                        // Half-Open 상태에서 성공하거나, 연속 성공 횟수가 충족되면 Healthy로 복구
                        if (old.Status == ProviderHealthStatus.CircuitBreakerHalfOpen || old.SuccessCount >= 3)
                        {
                            old.ErrorCount = 0;
                            old.Status = ProviderHealthStatus.Healthy;
                            old.CircuitBreakerResetTime = null;
                        }

                        // 누적 비용 추정치 업데이트
                        old.AccumulatedCost += (old.CostScore * (latencyMs / 1000.0));
                    }
                    old.LastUpdated = DateTime.UtcNow;
                    return old;
                });
        }

        /// <summary>
        /// 현재 모든 프로바이더의 메트릭 정보를 반환합니다.
        /// </summary>
        public IEnumerable<ProviderMetric> GetMetrics() => _metrics.Values;
    }
}
