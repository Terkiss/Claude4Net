using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.SDK;
using Claude4Net.Runtime;

namespace Claude4Net.Tests
{
    /// <summary>
    /// K033 Skill and Hook Operations 테스트
    /// 훅 파이프라인, Before/After/OnError 훅 동작, 체이닝, 실패 안전 처리를 검증합니다.
    /// </summary>
    public class K033SkillHookTests
    {
        // --- 테스트용 훅 구현 ---

        private class LoggingHook : IToolHook
        {
            public string Name => "LoggingHook";
            public HookTiming Timing { get; init; } = HookTiming.AfterToolExecution;
            public int Priority => 10;
            public bool IsEnabled { get; set; } = true;
            public List<string> Logs { get; } = new();

            public Task<HookResult> ExecuteAsync(HookContext context)
            {
                Logs.Add($"[{context.ToolName}] Result={context.Result}, Error={context.IsError}");
                return Task.FromResult(HookResult.Ok(Name));
            }
        }

        private class BlockingHook : IToolHook
        {
            public string Name => "BlockingHook";
            public HookTiming Timing => HookTiming.BeforeToolExecution;
            public int Priority => 1;
            public bool IsEnabled { get; set; } = true;
            public string BlockedTool { get; init; } = "dangerous_tool";

            public Task<HookResult> ExecuteAsync(HookContext context)
            {
                if (context.ToolName == BlockedTool)
                    return Task.FromResult(HookResult.Abort(Name, $"Tool '{context.ToolName}' is blocked"));
                return Task.FromResult(HookResult.Ok(Name));
            }
        }

        private class MetricsHook : IToolHook
        {
            public string Name => "MetricsHook";
            public HookTiming Timing => HookTiming.AfterToolExecution;
            public int Priority => 5;
            public bool IsEnabled { get; set; } = true;
            public int CallCount { get; private set; }

            public Task<HookResult> ExecuteAsync(HookContext context)
            {
                CallCount++;
                return Task.FromResult(HookResult.Ok(Name));
            }
        }

        private class ErrorRecoveryHook : IToolHook
        {
            public string Name => "ErrorRecoveryHook";
            public HookTiming Timing => HookTiming.OnToolError;
            public int Priority => 1;
            public bool IsEnabled { get; set; } = true;
            public List<string> Errors { get; } = new();

            public Task<HookResult> ExecuteAsync(HookContext context)
            {
                Errors.Add($"Error in {context.ToolName}: {context.Result}");
                return Task.FromResult(HookResult.Ok(Name));
            }
        }

        private class ThrowingHook : IToolHook
        {
            public string Name => "ThrowingHook";
            public HookTiming Timing { get; init; } = HookTiming.AfterToolExecution;
            public int Priority => 1;
            public bool IsEnabled { get; set; } = true;

            public Task<HookResult> ExecuteAsync(HookContext context)
            {
                throw new InvalidOperationException("Hook crashed!");
            }
        }

        private class OrderTrackingHook : IToolHook
        {
            public string Name { get; init; } = string.Empty;
            public HookTiming Timing { get; init; }
            public int Priority { get; init; }
            public bool IsEnabled { get; set; } = true;
            public List<string> ExecutionOrder { get; init; } = new();

            public Task<HookResult> ExecuteAsync(HookContext context)
            {
                ExecutionOrder.Add(Name);
                return Task.FromResult(HookResult.Ok(Name));
            }
        }

        // --- 테스트 메서드 ---

        [Fact]
        public async Task HookPipeline_RegisterAndExecute()
        {
            var pipeline = new HookPipeline();
            var loggingHook = new LoggingHook();
            pipeline.Register(loggingHook);

            Assert.Equal(1, pipeline.Count);

            var context = new HookContext { ToolName = "file_read", Result = "content" };
            var results = await pipeline.ExecuteAfterAsync(context);

            Assert.Single(results);
            Assert.True(results[0].Success);
            Assert.Single(loggingHook.Logs);
        }

        [Fact]
        public async Task HookPipeline_BeforeHookCanAbort()
        {
            var pipeline = new HookPipeline();
            pipeline.Register(new BlockingHook());

            var context = new HookContext { ToolName = "dangerous_tool" };
            var abortResult = await pipeline.ExecuteBeforeAsync(context);

            Assert.NotNull(abortResult);
            Assert.True(abortResult!.ShouldAbort);
            Assert.Contains("blocked", abortResult.AbortReason!);
        }

        [Fact]
        public async Task HookPipeline_BeforeHookAllowsNonBlocked()
        {
            var pipeline = new HookPipeline();
            pipeline.Register(new BlockingHook());

            var context = new HookContext { ToolName = "file_read" };
            var abortResult = await pipeline.ExecuteBeforeAsync(context);

            Assert.Null(abortResult);
        }

        [Fact]
        public async Task HookPipeline_AfterHookTracksMetrics()
        {
            var pipeline = new HookPipeline();
            var metricsHook = new MetricsHook();
            pipeline.Register(metricsHook);

            for (int i = 0; i < 5; i++)
            {
                await pipeline.ExecuteAfterAsync(new HookContext { ToolName = "bash" });
            }

            Assert.Equal(5, metricsHook.CallCount);
        }

        [Fact]
        public async Task HookPipeline_OnErrorHookTriggered()
        {
            var pipeline = new HookPipeline();
            var errorHook = new ErrorRecoveryHook();
            pipeline.Register(errorHook);

            var context = new HookContext { ToolName = "bash", Result = "command not found", IsError = true };
            await pipeline.ExecuteOnErrorAsync(context);

            Assert.Single(errorHook.Errors);
            Assert.Contains("command not found", errorHook.Errors[0]);
        }

        [Fact]
        public async Task HookPipeline_ChainingByPriority()
        {
            var executionOrder = new List<string>();
            var pipeline = new HookPipeline();

            pipeline.Register(new OrderTrackingHook { Name = "Third", Timing = HookTiming.AfterToolExecution, Priority = 30, ExecutionOrder = executionOrder });
            pipeline.Register(new OrderTrackingHook { Name = "First", Timing = HookTiming.AfterToolExecution, Priority = 10, ExecutionOrder = executionOrder });
            pipeline.Register(new OrderTrackingHook { Name = "Second", Timing = HookTiming.AfterToolExecution, Priority = 20, ExecutionOrder = executionOrder });

            await pipeline.ExecuteAfterAsync(new HookContext { ToolName = "test" });

            Assert.Equal(3, executionOrder.Count);
            Assert.Equal("First", executionOrder[0]);
            Assert.Equal("Second", executionOrder[1]);
            Assert.Equal("Third", executionOrder[2]);
        }

        [Fact]
        public async Task HookPipeline_FailSafeOnHookException()
        {
            var pipeline = new HookPipeline();
            var metricsHook = new MetricsHook();

            // ThrowingHook이 먼저 실행(Priority=1)되더라도 MetricsHook(Priority=5)은 실행됨
            pipeline.Register(new ThrowingHook());
            pipeline.Register(metricsHook);

            var results = await pipeline.ExecuteAfterAsync(new HookContext { ToolName = "test" });

            Assert.Equal(2, results.Count);
            Assert.False(results[0].Success); // ThrowingHook 실패
            Assert.Contains("Hook crashed", results[0].Error);
            Assert.True(results[1].Success); // MetricsHook 성공
            Assert.Equal(1, metricsHook.CallCount);
        }

        [Fact]
        public async Task HookPipeline_DisableHook()
        {
            var pipeline = new HookPipeline();
            var metricsHook = new MetricsHook();
            pipeline.Register(metricsHook);

            // 훅 비활성화
            Assert.True(pipeline.DisableHook("MetricsHook"));

            await pipeline.ExecuteAfterAsync(new HookContext { ToolName = "test" });
            Assert.Equal(0, metricsHook.CallCount); // 비활성화 상태이므로 실행 안됨

            // 훅 활성화
            Assert.True(pipeline.EnableHook("MetricsHook"));

            await pipeline.ExecuteAfterAsync(new HookContext { ToolName = "test" });
            Assert.Equal(1, metricsHook.CallCount); // 활성화 후 실행됨
        }

        [Fact]
        public void HookPipeline_FindHook()
        {
            var pipeline = new HookPipeline();
            pipeline.Register(new MetricsHook());
            pipeline.Register(new LoggingHook());

            Assert.NotNull(pipeline.FindHook("MetricsHook"));
            Assert.NotNull(pipeline.FindHook("LoggingHook"));
            Assert.Null(pipeline.FindHook("NonExistent"));
        }

        [Fact]
        public async Task HookPipeline_MixedTimingsOnlyExecuteCorrectOnes()
        {
            var pipeline = new HookPipeline();
            var beforeHook = new BlockingHook { BlockedTool = "none" };
            var afterHook = new MetricsHook();
            var errorHook = new ErrorRecoveryHook();

            pipeline.Register(beforeHook);
            pipeline.Register(afterHook);
            pipeline.Register(errorHook);

            // After 실행 시 Before/OnError 훅은 실행되지 않아야 함
            var results = await pipeline.ExecuteAfterAsync(new HookContext { ToolName = "test" });
            Assert.Single(results); // MetricsHook만
            Assert.Equal(1, afterHook.CallCount);
            Assert.Empty(errorHook.Errors);
        }

        [Fact]
        public void HookResult_FactoryMethods()
        {
            var ok = HookResult.Ok("test");
            Assert.True(ok.Success);
            Assert.False(ok.ShouldAbort);
            Assert.Null(ok.Error);

            var fail = HookResult.Fail("test", "something broke");
            Assert.False(fail.Success);
            Assert.Equal("something broke", fail.Error);

            var abort = HookResult.Abort("test", "blocked");
            Assert.True(abort.Success);
            Assert.True(abort.ShouldAbort);
            Assert.Equal("blocked", abort.AbortReason);
        }

        [Fact]
        public async Task HookPipeline_ContextMetadataShared()
        {
            var pipeline = new HookPipeline();
            pipeline.Register(new MetricsHook());

            var context = new HookContext
            {
                ToolName = "test",
                SessionId = "session-123",
                Metadata = { ["key"] = "value" }
            };

            var results = await pipeline.ExecuteAfterAsync(context);
            Assert.Single(results);
            Assert.Equal("value", context.Metadata["key"]);
        }
    }
}
