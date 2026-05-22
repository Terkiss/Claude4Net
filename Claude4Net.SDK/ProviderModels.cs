using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 라우팅 카테고리를 정의합니다.
    /// SmartRouter가 요청의 성격에 따라 적합한 프로바이더를 선택할 때 사용됩니다.
    /// </summary>
    [JsonConverter(typeof(RoutingCategoryJsonConverter))]
    public enum RoutingCategory
    {
        /// <summary> 간단한 버그 수정이나 빠른 작업 </summary>
        QuickFix,
        /// <summary> 복잡한 코드 분석과 리팩토링 </summary>
        DeepCode,
        /// <summary> 계획 수립과 설계 </summary>
        Planner,
        /// <summary> 검증 및 리뷰 </summary>
        Verifier,
        /// <summary> 이미지/시각 관련 작업 </summary>
        VisualEngineering,
        /// <summary> 문서 검색 및 참조 </summary>
        Librarian,
        /// <summary> 로컬 전용 작업 (프라이버시 요구) </summary>
        LocalPrivate,
        /// <summary> 저비용 유틸리티 작업 </summary>
        CheapUtility
    }

    /// <summary>
    /// RoutingCategory를 대소문자 구분 없이 파싱하고, 알 수 없는 카테고리가 입력되었을 때 예외를 던지는 JsonConverter입니다.
    /// </summary>
    public class RoutingCategoryJsonConverter : JsonConverter<RoutingCategory>
    {
        public override RoutingCategory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var val = reader.GetString();
                if (string.IsNullOrWhiteSpace(val))
                {
                    throw new JsonException("Routing category string value cannot be empty.");
                }

                if (Enum.TryParse<RoutingCategory>(val, true, out var result))
                {
                    return result;
                }
                throw new JsonException($"Unknown routing category: '{val}'");
            }
            throw new JsonException("Expected string value for RoutingCategory.");
        }

        public override void Write(Utf8JsonWriter writer, RoutingCategory value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    /// <summary>
    /// 프로바이더의 기능(capabilities)을 선언적으로 정의합니다.
    /// </summary>
    public sealed record ProviderCapabilities
    {
        /// <summary> 도구 호출(Function calling) 지원 여부 </summary>
        public bool ToolCalling { get; init; }
        /// <summary> 이미지 입력(Vision) 지원 여부 </summary>
        public bool Vision { get; init; }
        /// <summary> Thought signature 지원 여부 (Gemini) </summary>
        public bool ThoughtSignature { get; init; }
        /// <summary> 스트리밍 응답 지원 여부 </summary>
        public bool Streaming { get; init; }
        /// <summary> 임베딩 생성 지원 여부 </summary>
        public bool Embeddings { get; init; }
        /// <summary> 로컬 실행 여부 (네트워크 불필요) </summary>
        public bool Local { get; init; }
    }

    /// <summary>
    /// 프로바이더의 인증 방식을 정의합니다.
    /// </summary>
    public sealed record ProviderAuth
    {
        /// <summary> 인증 모드 (api-key, oauth, none) </summary>
        public string Mode { get; init; } = "api-key";
        /// <summary> 인증에 사용되는 환경 변수 목록 </summary>
        public IReadOnlyList<string> EnvVars { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// 프로바이더의 모델 기본값을 정의합니다.
    /// </summary>
    public sealed record ProviderDefaultModels
    {
        /// <summary> 빠른 응답용 소형 모델 </summary>
        public string Small { get; init; } = string.Empty;
        /// <summary> 복잡한 작업용 대형 모델 </summary>
        public string Large { get; init; } = string.Empty;
    }

    /// <summary>
    /// 프로바이더의 메타데이터를 선언적으로 정의하는 디스크립터입니다.
    /// SmartRouter, Doctor, Dashboard 등이 이 정보를 공유합니다.
    /// </summary>
    public sealed record ProviderDescriptor
    {
        /// <summary> 프로바이더 고유 ID (예: "gemini", "claude", "ollama") </summary>
        public string Id { get; init; } = string.Empty;
        /// <summary> 사용자에게 보여줄 표시 이름 </summary>
        public string Label { get; init; } = string.Empty;
        /// <summary> 전송 프로토콜 종류 (예: "gemini-native", "anthropic", "openai-compat", "cli") </summary>
        public string TransportKind { get; init; } = string.Empty;
        /// <summary> 기본 모델 설정 </summary>
        public ProviderDefaultModels DefaultModels { get; init; } = new();
        /// <summary> 프로바이더 기능 </summary>
        public ProviderCapabilities Capabilities { get; init; } = new();
        /// <summary> 인증 설정 </summary>
        public ProviderAuth Auth { get; init; } = new();
        /// <summary> 비용 점수 (0.0: 무료, 1.0: 고비용) </summary>
        public double CostScore { get; init; }
        /// <summary> 지원하는 라우팅 카테고리 목록 </summary>
        public IReadOnlyList<RoutingCategory> SupportedCategories { get; init; } = Array.Empty<RoutingCategory>();
        /// <summary> 컨텍스트 윈도우 크기 (토큰 수) </summary>
        public int ContextWindowSize { get; init; }

        // V2 Fields
        /// <summary> 프로바이더 API 엔드포인트 또는 베이스 URL </summary>
        public string Endpoint { get; init; } = string.Empty;
        /// <summary> 추가 HTTP 헤더 설정 </summary>
        public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary> 라우팅 및 트랜스포트 확장을 위한 메타데이터 </summary>
        public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
    }
}
