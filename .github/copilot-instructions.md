# AI reviewer & assistant instructions

House rules for every AI bot that touches this repository — GitHub Copilot (chat + PR
review), CodeRabbit, and Gemini Code Assist. **Review aggressively.** Surface every real
defect: correctness, security, performance, accessibility, and maintainability. Don't soften
or withhold findings; give concrete, copy-pasteable fixes over vague advice. Assume code was
AI-generated and may not actually work — verify the logic, don't trust the prose.

These are owner-wide defaults. Apply the sections that match this repo's actual stack; ignore
the rest.

## Review priorities (in order)
1. **Correctness** — does the change do what it claims? Trace logic and edge cases.
2. **Security** — injection, XSS, auth/authz, CORS, secret leakage, unsafe deserialization.
3. **Accessibility** — for any UI; a11y regressions are bugs, not nits.
4. **Performance** — query shape (N+1), blocking calls, render churn, allocations.
5. **Tests & maintainability** — real coverage, clear naming, no dead code.

## Security (all languages)
- Never trust external / user / agent input — validate and sanitize at the boundary.
- No secrets in source or committed config.
- Parameterized queries only; never string-build SQL.
- No `eval` / dynamic exec on untrusted data; safe deserialization only.
- CORS must not be wide-open (`AllowAnyOrigin` + credentials) unless documented as local-only.

## C# / .NET (+ Blazor)
- Nullable reference types; flag null derefs and missing guard clauses.
- async: no `async void` (except event handlers); no `.Result` / `.Wait()` /
  `.GetAwaiter().GetResult()`; thread `CancellationToken`; `ConfigureAwait(false)` in libraries.
- DI: correct lifetimes; no captive dependencies (singleton holding scoped/transient).
- EF Core: no N+1; `AsNoTracking()` for reads; parameterized; schema via migrations, not manual SQL.
- Dispose owned `IDisposable` / `IAsyncDisposable`.
- Blazor: no blocking I/O in `OnInitialized`; dispose subscriptions; treat `[Parameter]` as
  immutable; sanitize `MarkupString` / raw HTML; ARIA + keyboard + focus handling; `@key` in loops.

## Python
- PEP 8; type hints on public APIs; specific exceptions (no bare `except:`).
- Context managers for resources; no mutable default arguments.
- No `pickle` / unsafe deserialization or `subprocess(shell=True)` on untrusted input.
- Async: don't block the event loop; await all coroutines.

## JavaScript / TypeScript / CSS
- Prevent XSS (sanitize before DOM insertion); no `eval` / `new Function`; no `innerHTML` with
  untrusted data.
- TypeScript: strict typing, no implicit `any`, null-safety; verify React hook usage and a11y.
- Handle promise rejections; no leftover `console.*` in shipped code.
- CSS: avoid `!important`; reuse design tokens; responsive + dark-mode; WCAG AA contrast.

## PowerShell
- Strict error handling (`$ErrorActionPreference = 'Stop'` / `-ErrorAction Stop`) + try/catch.
- No plaintext secrets; validate parameters; never `Invoke-Expression` untrusted input; use
  approved verbs; keep operations idempotent.

## Tests
Tests MUST exercise real code paths. Reject fake/placeholder tests, assertions against mocks
only, and trivially-true assertions. Cover edge cases, error paths, and boundaries; keep tests
deterministic; mock only true external boundaries; flag new or changed behavior shipped without
coverage.

## GitHub Actions (only if workflows already exist)
Least-privilege `permissions:`; pin third-party actions to a full commit SHA; never interpolate
untrusted `${{ github.event.* }}` text into `run:` shells; beware `pull_request_target` with PR
checkout; set job timeouts.
