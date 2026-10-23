## Summary

<!-- What does this change and why? -->

Closes #

## Type of change

- [ ] Bug fix
- [ ] New feature or roadmap milestone work
- [ ] Refactoring (no behavior change)
- [ ] Documentation, build or CI

## Checklist

- [ ] Spec requirement IDs and the roadmap milestone are named above, where they apply.
- [ ] New tests carry the requirement IDs they cover in `[Description]` (`ArchitectureTests.EveryRequirement_HasATest`).
- [ ] `dotnet build BB84.MPX1.slnx -c Release` passes (warnings are errors, no IL2xxx/IL3xxx warnings).
- [ ] `dotnet test --solution BB84.MPX1.slnx -c Release -- --ignore-exit-code 8 --filter "TestCategory!=Hardware"` passes.
- [ ] No parameter-type numbers that differ between V1.00 and V1.10 are hard-coded.
- [ ] No code, text or resources from `legacy/MPX1_V1/` are used (clean room).
- [ ] `CHANGELOG.md` is updated, if the change is visible to users.
