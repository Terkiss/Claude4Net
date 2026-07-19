using System.Collections.Generic;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime.Services
{
    public enum FailurePattern
    {
        None,
        ToolUsageError,
        SecurityRejection,
        InfiniteLoop,
        Hallucination
    }

    public class HealingDirective
    {
        public FailurePattern Pattern { get; set; }
        public string Instruction { get; set; } = string.Empty;
        public bool IsActive => true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class RecoveryPrescription
    {
        public RefinedErrorCategory Category { get; set; }
        public string Recommendation { get; set; } = string.Empty;
        public RetryPolicy? RetryPolicy { get; set; }
        public string? SuggestedModel { get; set; }
        public string? SuggestedPromptAdjustment { get; set; }
    }

    public interface ISelfHealingService
    {
        int CurrentReflectionDepth { get; }
        bool IncrementReflectionDepth();
        void ResetReflectionDepth();
        FailurePattern ClassifyPattern(IEnumerable<object> events);
        HealingDirective GenerateDirective(FailurePattern pattern);
        string GetGuide();
        void UpdateGuide(string reflectionSummary);
        Task PruneTrajectoriesAsync(int keepDays = 7);
        RecoveryPrescription RecommendRecovery(RefinedErrorCategory category, string toolName, string error);
    }
}
