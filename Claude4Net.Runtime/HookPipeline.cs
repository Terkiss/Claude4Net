using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// 도구 실행 훅 파이프라인입니다.
    /// 등록된 훅을 우선순위 순으로 실행하며, Before 훅의 중단 요청을 처리합니다.
    /// 개별 훅의 실패는 파이프라인을 중단하지 않습니다 (fail-safe).
    /// </summary>
    public sealed class HookPipeline
    {
        private readonly List<IToolHook> _hooks = new();

        /// <summary>
        /// 등록된 모든 훅을 반환합니다.
        /// </summary>
        public IReadOnlyList<IToolHook> Hooks => _hooks.AsReadOnly();

        /// <summary>
        /// 훅을 등록합니다.
        /// </summary>
        public HookPipeline Register(IToolHook hook)
        {
            if (hook == null) throw new ArgumentNullException(nameof(hook));
            _hooks.Add(hook);
            return this;
        }

        /// <summary>
        /// 복수의 훅을 한번에 등록합니다.
        /// </summary>
        public HookPipeline RegisterAll(IEnumerable<IToolHook> hooks)
        {
            foreach (var hook in hooks) Register(hook);
            return this;
        }

        /// <summary>
        /// 특정 시점의 훅들을 우선순위 순으로 실행합니다.
        /// </summary>
        /// <param name="timing">실행 시점</param>
        /// <param name="context">훅 컨텍스트</param>
        /// <returns>실행된 훅들의 결과 목록</returns>
        public async Task<IReadOnlyList<HookResult>> ExecuteAsync(HookTiming timing, HookContext context)
        {
            var results = new List<HookResult>();

            var applicable = _hooks
                .Where(h => h.Timing == timing && h.IsEnabled)
                .OrderBy(h => h.Priority)
                .ToList();

            foreach (var hook in applicable)
            {
                try
                {
                    var result = await hook.ExecuteAsync(context);
                    results.Add(result);

                    // Before 훅이 중단을 요청하면 나머지 훅을 실행하지 않음
                    if (timing == HookTiming.BeforeToolExecution && result.ShouldAbort)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    // 개별 훅 실패는 파이프라인을 중단하지 않음 (fail-safe)
                    results.Add(HookResult.Fail(hook.Name, ex.Message));
                }
            }

            return results.AsReadOnly();
        }

        /// <summary>
        /// Before 훅을 실행하고 중단 여부를 반환합니다.
        /// </summary>
        /// <returns>중단해야 하면 HookResult (ShouldAbort=true), 아니면 null</returns>
        public async Task<HookResult?> ExecuteBeforeAsync(HookContext context)
        {
            var results = await ExecuteAsync(HookTiming.BeforeToolExecution, context);
            return results.FirstOrDefault(r => r.ShouldAbort);
        }

        /// <summary>
        /// After 훅을 실행합니다.
        /// </summary>
        public async Task<IReadOnlyList<HookResult>> ExecuteAfterAsync(HookContext context)
        {
            return await ExecuteAsync(HookTiming.AfterToolExecution, context);
        }

        /// <summary>
        /// OnError 훅을 실행합니다.
        /// </summary>
        public async Task<IReadOnlyList<HookResult>> ExecuteOnErrorAsync(HookContext context)
        {
            return await ExecuteAsync(HookTiming.OnToolError, context);
        }

        /// <summary>
        /// 이름으로 훅을 찾습니다.
        /// </summary>
        public IToolHook? FindHook(string name)
        {
            return _hooks.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 훅을 비활성화합니다.
        /// </summary>
        public bool DisableHook(string name)
        {
            var hook = FindHook(name);
            if (hook == null) return false;
            hook.IsEnabled = false;
            return true;
        }

        /// <summary>
        /// 훅을 활성화합니다.
        /// </summary>
        public bool EnableHook(string name)
        {
            var hook = FindHook(name);
            if (hook == null) return false;
            hook.IsEnabled = true;
            return true;
        }

        /// <summary>
        /// 등록된 훅 수를 반환합니다.
        /// </summary>
        public int Count => _hooks.Count;
    }
}
