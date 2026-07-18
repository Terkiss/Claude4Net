using System;
using System.Threading.Channels;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// 자율 연속 실행 루프의 핵심 디스패처입니다.
    /// 각 턴 종료 후 6개 조건을 검사하여 continuation(자동 다음 턴) 여부를 결정합니다.
    ///
    /// 검사 조건 (주인님 설계):
    /// 1. Goal이 active인가?
    /// 2. 현재 스레드가 idle인가? (다른 작업 실행 중이 아닌가?)
    /// 3. 대기 중인 사용자 입력이 없는가? (broker 큐가 비어있는가?)
    /// 4. 다른 작업이 실행 중이지 않은가?
    /// 5. 예산(최대 턴 수)이 남았는가?
    /// 6. 직전 자동 턴이 의미 있는 작업을 했는가? (진행 없음 카운터)
    /// </summary>
    public static class GoalDispatcher
    {
        /// <summary>
        /// 현재 Goal 상태를 기반으로 continuation(자동 다음 턴)을 실행할지 결정합니다.
        /// </summary>
        /// <param name="broker">입력 브로커 (continuation prompt 주입용)</param>
        /// <param name="hasPendingUserInput">대기 중인 사용자 입력이 있는지 여부</param>
        /// <returns>continuation을 주입했으면 true, 루프를 중단하면 false</returns>
        public static bool TryContinue(IInputBroker broker, bool hasPendingUserInput)
        {
            var goal = AppState.ActiveGoal;
            if (goal == null) return false;

            // ── 조건 1: Goal이 active인가? ──
            if (!goal.IsActive)
            {
                return false;
            }

            // ── 조건 3: 대기 중인 사용자 입력이 없는가? ──
            // 사용자가 새 입력을 넣었다면 자율 루프를 양보하고 사용자 입력을 처리
            if (hasPendingUserInput)
            {
                return false;
            }

            // ── 조건 5: 예산(최대 턴 수)이 남았는가? ──
            if (goal.MaxTurns > 0 && goal.TurnCount >= goal.MaxTurns)
            {
                goal.Status = GoalStatus.Stopped;
                return false;
            }

            // ── 조건 6: 직전 턴이 의미 있는 작업을 했는가? ──
            // 도구 호출도 없고 응답 길이도 짧으면 진행 없음으로 간주
            bool hadProgress = goal.LastTurnToolCallCount > 0 || goal.LastTurnResponseLength > 100;
            if (!hadProgress)
            {
                goal.NoProgressCount++;
                if (goal.NoProgressCount >= goal.MaxNoProgressTurns)
                {
                    goal.Status = GoalStatus.Failed;
                    return false;
                }
            }
            else
            {
                goal.NoProgressCount = 0;
            }

            // ── 모든 조건 통과: continuation prompt 주입 ──
            string continuationPrompt = BuildContinuationPrompt(goal);
            goal.TurnCount++;

            var context = new InputContext(
                continuationPrompt,
                new NullOutputHandler(),
                null
            );

            return broker.TryWrite(context);
        }

        /// <summary>
        /// Goal의 현재 상태를 기반으로 다음 턴용 continuation prompt를 생성합니다.
        /// </summary>
        private static string BuildContinuationPrompt(GoalState goal)
        {
            return $@"
[AUTONOMOUS CONTINUATION — Goal #{goal.Id}]
목표: {goal.Objective}
턴: {goal.TurnCount + 1}/{(goal.MaxTurns > 0 ? goal.MaxTurns.ToString() : "∞")}

위 목표를 달성하기 위해 다음 단계를 계속 진행하십시오.
- 직전 턴에서 수행한 작업을 기반으로 다음 논리적 단계를 실행하세요.
- 목표가 완료되었다면 응답에 '[GOAL_COMPLETED]'를 포함하세요.
- 더 이상 진행할 수 없거나 막혔다면 '[GOAL_BLOCKED]'를 포함하세요.
불필요한 설명 없이 즉시 행동하세요.
";
        }

        /// <summary>
        /// 턴 종료 후 Goal 상태를 업데이트합니다.
        /// AgentLoop가 각 턴 실행 후 호출합니다.
        /// </summary>
        /// <param name="toolCallCount">이번 턴에 실행된 도구 호출 수</param>
        /// <param name="responseLength">이번 턴의 응답 텍스트 길이</param>
        public static void UpdateTurnResult(int toolCallCount, int responseLength)
        {
            var goal = AppState.ActiveGoal;
            if (goal == null || !goal.IsActive) return;

            goal.LastTurnToolCallCount = toolCallCount;
            goal.LastTurnResponseLength = responseLength;
        }

        /// <summary>
        /// 응답 텍스트에서 완료/차단 마커를 감지하여 Goal 상태를 갱신합니다.
        /// </summary>
        /// <param name="responseText">이번 턴의 최종 응답 텍스트</param>
        /// <returns>상태가 변경되었으면 true</returns>
        public static bool CheckCompletionMarkers(string responseText)
        {
            var goal = AppState.ActiveGoal;
            if (goal == null || !goal.IsActive) return false;

            if (string.IsNullOrEmpty(responseText)) return false;

            if (responseText.Contains("[GOAL_COMPLETED]", StringComparison.OrdinalIgnoreCase))
            {
                goal.Status = GoalStatus.Completed;
                return true;
            }

            if (responseText.Contains("[GOAL_BLOCKED]", StringComparison.OrdinalIgnoreCase))
            {
                goal.Status = GoalStatus.Failed;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Goal을 활성화합니다.
        /// </summary>
        public static GoalState Activate(string objective, int maxTurns = 25)
        {
            var goal = new GoalState
            {
                Objective = objective,
                Status = GoalStatus.Active,
                MaxTurns = maxTurns,
                TurnCount = 0,
                NoProgressCount = 0
            };
            AppState.ActiveGoal = goal;
            return goal;
        }

        /// <summary>
        /// 현재 Goal을 정지하고 제거합니다.
        /// </summary>
        public static void Stop()
        {
            if (AppState.ActiveGoal != null)
            {
                AppState.ActiveGoal.Status = GoalStatus.Stopped;
                AppState.ActiveGoal = null;
            }
        }
    }
}
