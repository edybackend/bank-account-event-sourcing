# Engineering rules for AI agents

This file is loaded by Claude Code in every session. It defines how changes are made in this repository.
Hooks in `.claude/settings.json` enforce the parts that can be automated.

## Workflow
1. **Spec first.** A new behavior starts as a spec in `specs/NNN-name.md` (context, behavior, acceptance criteria, out of scope). Code follows the spec.
2. **Small steps.** One concern per commit, [Conventional Commits](https://www.conventionalcommits.org/) (`feat(domain): ...`, `fix(api): ...`).
3. **Definition of done** = the CI gates pass locally: `dotnet build` (warnings are errors), `dotnet test`, `dotnet format --verify-no-changes`. The Stop hook blocks finishing while they are red.

## Architecture boundaries (enforced by `tests/BankAccount.ArchitectureTests`)
- `BankAccount.Domain` depends on nothing. The `Account` aggregate, `Money` and the events live here.
- `BankAccount.Application` depends only on Domain. It declares `IEventStore` and `IAccountReadModel`; no Dapper, SQLite or ASP.NET Core.
- `BankAccount.Infrastructure` implements them with SQLite and Dapper. `BankAccount.Api` only maps HTTP; endpoints never touch the database.

## Event sourcing rules
- **Events are the source of truth and are never updated or deleted.** A correction is a new event.
- **Stored events are a contract.** Never rename or remove an event type, a stored type name in `EventSerializer` or a field of an event. Add a new optional field or a new event instead. A hook blocks removals in `AccountEvents.cs`.
- Events carry primitives only (no value objects), so refactoring the domain never breaks old streams.
- State changes only in `Account.Apply`. Command methods validate, then `Raise` an event. Replaying the stream must rebuild exactly the same state.
- Appends always pass the version that was read. On a conflict, run the whole command again on the fresh stream (the handler does this, up to 3 times); never append without the check.
- Projections are updated in the same transaction as the events. A new read model must be derivable from the events alone.
- Money in read models is stored in cents (`INTEGER`). Never use floating point for money.

## Error handling
- Expected failures are values (`Result<T>` with a `Failure`). HTTP status codes are decided only in `FailureResults`: 400 input, 404 unknown account, 409 conflict after retries, 422 business rule.

## C# conventions
- Classes `sealed` by default; file-scoped namespaces; `TimeProvider` instead of `DateTime.Now`; `Guid.CreateVersion7()` for ids.
- No MediatR/AutoMapper. Logging with `[LoggerMessage]`.

## Never
- Edit the database files or append events outside `IEventStore`. Blocked by hook.
- Read or write secrets (`.env`, keys). Blocked by permissions and hook.
- Disable an analyzer or a test to make the build pass.
- Add a dependency without a reason stated in the commit message.
