# 003 · Read models and balance history

## Context
CQRS: commands write events; queries read projections built from those events. Event sourcing also makes it possible to answer "what was the balance on a given date?" by replaying the stream up to that moment.

## Behavior
- Projections are updated in the same transaction as the events they come from, so a query right after a command sees its effect.
- `GET /api/accounts` lists account summaries (owner, currency, balance, status).
- `GET /api/accounts/{id}` returns one summary with the stream version.
- `GET /api/accounts/{id}/statement?page=&pageSize=` returns statement lines, newest first, each with the signed amount and the balance after it.
- `GET /api/accounts/{id}/balance?at=<ISO 8601>` replays the stream up to `at` and returns the balance at that moment.

## Acceptance criteria
- The summary balance equals the balance obtained by replaying the whole stream.
- The sum of the statement amounts equals the summary balance.
- `balance?at=` before the account was opened returns `404`; without `at` it returns the current balance.

## Out of scope
Asynchronous projections, rebuilding projections from scratch via the API, snapshots.
