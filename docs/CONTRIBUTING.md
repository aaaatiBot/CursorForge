# Contributing to CursorForge

Thank you for your interest in contributing to CursorForge! 

This project follows rigorous architectural guidelines, strict code hygiene, and test-driven development.

---

## 1. Development Principles

1. **Safety First**: Modifying Windows system cursors and the Windows Registry has the potential to impact desktop usability. All code interacting with the registry must support snapshots, rollbacks, and dynamic default resolution.
2. **Deterministic Warnings**: The solution compiles with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. Code with compiler warnings will fail CI.
3. **No PII Logging**: Never log passwords, recovery codes, auth tokens, email addresses, or unscrubbed user file paths. Use `StructuredLogger` exclusively.

---

## 2. Commit Message Standards

Commit messages must follow the [Conventional Commits](https://www.conventionalcommits.org/) format:

```
<type>(<scope>): <short description>
```

### Types:
- `feat`: A new user-facing feature or capability.
- `fix`: A bug fix.
- `refactor`: Code change that neither fixes a bug nor adds a feature.
- `test`: Adding or correcting tests.
- `docs`: Documentation updates.
- `perf`: Performance improvements.
- `ci`: CI/CD pipeline changes.

### Examples:
- `feat(engine): add 128px multi-resolution DIB tier support`
- `fix(registry): handle missing value types gracefully during snapshot`
- `docs(api): document rate limiting RPC parameters`

---

## 3. Pull Request Checklist

Before submitting a PR, verify:
- [ ] `dotnet build CursorForge.slnx -warnaserror` succeeds with 0 errors and 0 warnings.
- [ ] All unit and integration tests pass (`dotnet test CursorForge.Tests\CursorForge.Tests.csproj --no-restore`).
- [ ] New functionality is covered by automated unit/integration tests.
- [ ] Any changed SQL schemas or Edge Functions include adversarial test scripts in `supabase/tests/`.
- [ ] Documentation in `docs/` is updated to reflect new APIs or behaviors.
