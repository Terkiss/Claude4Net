using Xunit;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Claude4Net.Tests
{
    public class GoalDispatcherTests
    {
        public GoalDispatcherTests()
        {
            // 각 테스트 전에 ActiveGoal 초기화
            AppState.ActiveGoal = null;
        }

        // ──────────────────────────────────────────────
        // GoalState 모델
        // ──────────────────────────────────────────────

        [Fact]
        public void GoalState_DefaultValues_AreCorrect()
        {
            var goal = new GoalState { Objective = "test" };

            Assert.Equal(GoalStatus.Active, goal.Status);
            Assert.True(goal.IsActive);
            Assert.False(goal.IsCompleted);
            Assert.Equal(0, goal.TurnCount);
            Assert.Equal(25, goal.MaxTurns);
            Assert.Equal(3, goal.MaxNoProgressTurns);
        }

        // ──────────────────────────────────────────────
        // Activate / Stop
        // ──────────────────────────────────────────────

        [Fact]
        public void Activate_SetsActiveGoal()
        {
            var goal = GoalDispatcher.Activate("refactor module X", maxTurns: 10);

            Assert.NotNull(AppState.ActiveGoal);
            Assert.Equal("refactor module X", AppState.ActiveGoal!.Objective);
            Assert.Equal(GoalStatus.Active, AppState.ActiveGoal.Status);
            Assert.Equal(10, AppState.ActiveGoal.MaxTurns);
        }

        [Fact]
        public void Stop_ClearsActiveGoal()
        {
            GoalDispatcher.Activate("test objective");
            Assert.NotNull(AppState.ActiveGoal);

            GoalDispatcher.Stop();

            // Stop은 상태를 Stopped로 변경
            Assert.Null(AppState.ActiveGoal);
        }

        // ──────────────────────────────────────────────
        // TryContinue — 조건 검사
        // ──────────────────────────────────────────────

        [Fact]
        public void TryContinue_ReturnsFalse_WhenNoActiveGoal()
        {
            var broker = new ChannelBroker();
            Assert.False(GoalDispatcher.TryContinue(broker, false));
        }

        [Fact]
        public void TryContinue_ReturnsFalse_WhenGoalNotActive()
        {
            var broker = new ChannelBroker();
            var goal = GoalDispatcher.Activate("test");
            goal.Status = GoalStatus.Completed;

            Assert.False(GoalDispatcher.TryContinue(broker, false));
        }

        [Fact]
        public void TryContinue_ReturnsFalse_WhenPendingUserInput()
        {
            var broker = new ChannelBroker();
            GoalDispatcher.Activate("test");

            // 사용자 입력이 대기 중이면 continuation 불가
            Assert.False(GoalDispatcher.TryContinue(broker, true));
        }

        [Fact]
        public void TryContinue_ReturnsFalse_WhenBudgetExhausted()
        {
            var broker = new ChannelBroker();
            var goal = GoalDispatcher.Activate("test", maxTurns: 3);
            goal.TurnCount = 3;

            Assert.False(GoalDispatcher.TryContinue(broker, false));
            Assert.Equal(GoalStatus.Stopped, AppState.ActiveGoal!.Status);
        }

        [Fact]
        public void TryContinue_ReturnsFalse_WhenMaxNoProgressExceeded()
        {
            var broker = new ChannelBroker();
            var goal = GoalDispatcher.Activate("test");
            goal.LastTurnToolCallCount = 0;
            goal.LastTurnResponseLength = 10; // 짧은 응답 = 진행 없음

            // MaxNoProgressTurns(3)까지 진행 없음 누적
            Assert.True(GoalDispatcher.TryContinue(broker, false)); // NoProgress=1, 주입
            AppState.ActiveGoal!.LastTurnToolCallCount = 0;
            AppState.ActiveGoal.LastTurnResponseLength = 10;

            Assert.True(GoalDispatcher.TryContinue(broker, false)); // NoProgress=2, 주입
            AppState.ActiveGoal!.LastTurnToolCallCount = 0;
            AppState.ActiveGoal.LastTurnResponseLength = 10;

            Assert.False(GoalDispatcher.TryContinue(broker, false)); // NoProgress=3, 실패
            Assert.Equal(GoalStatus.Failed, AppState.ActiveGoal!.Status);
        }

        [Fact]
        public async Task TryContinue_InjectsContinuation_WhenAllConditionsMet()
        {
            var broker = new ChannelBroker();
            var goal = GoalDispatcher.Activate("do work");
            goal.LastTurnToolCallCount = 2; // 의미 있는 작업
            goal.LastTurnResponseLength = 500;

            bool result = GoalDispatcher.TryContinue(broker, false);

            // TryWrite가 성공해야 함
            Assert.True(result, "TryContinue should return true when all conditions are met");

            // continuation이 큐에서 읽히는지 확인 (PendingCount 대신 ReadAsync로 검증)
            var context = await broker.ReadAsync(CancellationToken.None);
            Assert.NotNull(context);
            Assert.Contains("AUTONOMOUS CONTINUATION", context.Text);
            Assert.Contains("do work", context.Text);
        }

        [Fact]
        public void TryContinue_ResetsNoProgress_WhenHadProgress()
        {
            var broker = new ChannelBroker();
            var goal = GoalDispatcher.Activate("test");
            goal.NoProgressCount = 2; // 거의 한도 도달
            goal.LastTurnToolCallCount = 3; // 이번엔 진행했음
            goal.LastTurnResponseLength = 600;

            // 먼저 drain
            bool result = GoalDispatcher.TryContinue(broker, false);

            Assert.True(result);
            Assert.Equal(0, AppState.ActiveGoal!.NoProgressCount); // 리셋됨
        }

        // ──────────────────────────────────────────────
        // UpdateTurnResult
        // ──────────────────────────────────────────────

        [Fact]
        public void UpdateTurnResult_UpdatesGoalFields()
        {
            var goal = GoalDispatcher.Activate("test");

            GoalDispatcher.UpdateTurnResult(toolCallCount: 5, responseLength: 1234);

            Assert.Equal(5, goal.LastTurnToolCallCount);
            Assert.Equal(1234, goal.LastTurnResponseLength);
        }

        [Fact]
        public void UpdateTurnResult_DoesNothing_WhenNoActiveGoal()
        {
            AppState.ActiveGoal = null;
            // 예외 없이 통과해야 함
            GoalDispatcher.UpdateTurnResult(5, 100);
        }

        // ──────────────────────────────────────────────
        // CheckCompletionMarkers
        // ──────────────────────────────────────────────

        [Fact]
        public void CheckCompletionMarkers_DetectsCompleted()
        {
            GoalDispatcher.Activate("test");

            bool changed = GoalDispatcher.CheckCompletionMarkers("All done! [GOAL_COMPLETED]");

            Assert.True(changed);
            Assert.Equal(GoalStatus.Completed, AppState.ActiveGoal!.Status);
        }

        [Fact]
        public void CheckCompletionMarkers_DetectsBlocked()
        {
            GoalDispatcher.Activate("test");

            bool changed = GoalDispatcher.CheckCompletionMarkers("Can't proceed [GOAL_BLOCKED]");

            Assert.True(changed);
            Assert.Equal(GoalStatus.Failed, AppState.ActiveGoal!.Status);
        }

        [Fact]
        public void CheckCompletionMarkers_ReturnsFalse_WhenNoMarker()
        {
            GoalDispatcher.Activate("test");

            bool changed = GoalDispatcher.CheckCompletionMarkers("Still working...");

            Assert.False(changed);
            Assert.True(AppState.ActiveGoal!.IsActive);
        }

        [Fact]
        public void CheckCompletionMarkers_ReturnsFalse_WhenNoGoal()
        {
            AppState.ActiveGoal = null;
            Assert.False(GoalDispatcher.CheckCompletionMarkers("anything"));
        }

        // ──────────────────────────────────────────────
        // ChannelBroker.PendingCount
        // ──────────────────────────────────────────────

        [Fact]
        public async Task ChannelBroker_PendingCount_ReflectsQueueDepth()
        {
            var broker = new ChannelBroker();

            Assert.Equal(0, broker.PendingCount);

            broker.TryWrite(new InputContext("msg1", new NullOutputHandler()));
            broker.TryWrite(new InputContext("msg2", new NullOutputHandler()));

            Assert.Equal(2, broker.PendingCount);

            await broker.ReadAsync(CancellationToken.None);

            Assert.Equal(1, broker.PendingCount);
        }
    }
}
