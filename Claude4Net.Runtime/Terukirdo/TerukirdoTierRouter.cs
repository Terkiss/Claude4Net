using System;
using System.Collections.Generic;
using System.Linq;
using Claude4Net.SDK.Terukirdo;

namespace Claude4Net.Runtime.Terukirdo
{
    /// <summary>
    /// 의도 및 위험도 분석을 통한 4단계 적응형 루프 라우터 구현체
    /// </summary>
    public class TerukirdoTierRouter : ITerukirdoTierRouter
    {
        private static readonly HashSet<string> HighRiskKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "auth", "login", "password", "secret", "token", "credential", "api-key",
            "migration", "drop table", "truncate", "delete from", "rm -rf", "push --force",
            "deploy", "production", "release", "rollback", "incident", "format c:"
        };

        private static readonly HashSet<string> MinorDocOrTypoKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "오탈자", "typo", "주석", "comment", "단순 문서", "단순 수정", "readme 수정", "오타"
        };

        private static readonly HashSet<string> ConversationalPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "안녕", "hello", "hi", "고마워", "수고했어", "오늘 날씨", "누구야", "자기소개", "몇 시야", "뭐해"
        };

        public AdaptiveLoopTier ClassifyIntent(string prompt, TerukirdoMode mode, AdaptiveLoopTier? explicitTier = null)
        {
            // 1. 명시적 티어 지정이 있는 경우 최우선 적용
            if (explicitTier.HasValue)
            {
                return explicitTier.Value;
            }

            // 2. 모드가 Companion 또는 MaidSecretary일 경우 Tier 0 강제
            if (mode == TerukirdoMode.Companion || mode == TerukirdoMode.MaidSecretary)
            {
                return AdaptiveLoopTier.Tier0_Companion;
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return AdaptiveLoopTier.Tier0_Companion;
            }

            string trimmed = prompt.Trim();

            // 3. Tier 3 (High Risk) 키워드 또는 위험 패턴 검사
            if (HighRiskKeywords.Any(k => trimmed.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return AdaptiveLoopTier.Tier3_HighRisk_Release;
            }

            // 4. Tier 0 (Companion / 대화) 패턴 검사
            if (ConversationalPrefixes.Any(p => trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase)) && trimmed.Length < 40)
            {
                return AdaptiveLoopTier.Tier0_Companion;
            }

            // 5. Tier 1 (Low Risk / 단순 문서) 패턴 검사
            if (MinorDocOrTypoKeywords.Any(k => trimmed.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                !trimmed.Contains("구현", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.Contains("아키텍처", StringComparison.OrdinalIgnoreCase))
            {
                return AdaptiveLoopTier.Tier1_LowRisk;
            }

            // 6. 기본값: Tier 2 (Medium Risk - Ralph Loop 가동)
            return AdaptiveLoopTier.Tier2_MediumRisk_RalphLoop;
        }
    }
}
