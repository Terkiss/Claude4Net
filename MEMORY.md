# Terukirdo Memory

## Current Status

- Repository branch: `experiment`
- Latest pushed commit: `30fa21d`
- Provider extensions, repository hygiene changes, harness configuration, and documentation are committed and pushed.
- The working tree was clean after the last push.

## Key Learnings

- Build and test verification should run before committing repository-wide changes.
- Generated artifacts such as `bin/`, `obj/`, test results, local Android properties, and local SDK bundles must remain ignored.
- Several legacy C# files contain invalid or mixed encoding; comment rewrites must preserve source bytes and be performed in small, verifiable scopes.
- Secret-pattern matches in security tests may be intentional fixtures and require context review before treating them as leaked credentials.
- `SystemPromptBuilder` depends on `.resources/` being copied into the test/app output; removing the tracked resource fixtures breaks the real-resource regression test.

## Open Questions

- Whether the large harness and skillopt import should be split into focused commits in a future cleanup.
- Whether legacy source files should undergo a dedicated encoding migration with a complete test pass.

## Next Steps

- Keep trajectory and memory files updated after meaningful implementation or verification work.
- Run the repository quality gates before the next release-oriented change.
