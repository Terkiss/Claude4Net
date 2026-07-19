# SELF_HEAL_GUIDE
> Last Updated: 2026-07-19 09:07:21

## Self-Reflection Analysis
Test failure in bash tool due to permission.

## Execution Guardrails
1. **Path Safety**: Always verify directory existence before writing files.
2. **Build Integrity**: Run `dotnet build` after significant code changes.
3. **Retry Strategy**: If a tool fails with a 'Permission' or 'Quota' error, follow the recommended backoff.
4. **Context Management**: If an error persists, use `!clear` or `reset` to refresh the agent context.

## Recommended Retry Policies
- **ProviderError**: FixedInterval (Max 3 retries, 1000ms base delay)
- **ToolFailure**: Immediate (Max 1 retries, 1000ms base delay)
- **NetworkError**: ExponentialBackoff (Max 3 retries, 2000ms base delay)
- **TimeoutError**: FixedInterval (Max 2 retries, 3000ms base delay)
- **QuotaError**: ExponentialBackoff (Max 5 retries, 5000ms base delay)
