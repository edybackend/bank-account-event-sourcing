# 001 · Open an account and move money

## Context
A bank account is event sourced: its state is never stored directly. Every change is an event in the account's stream, and the current state is rebuilt by replaying the stream.

## Behavior
- `POST /api/accounts` with `owner` and `currency` (`BRL`, `USD` or `EUR`) opens an account with balance 0 and a daily withdrawal limit of 5,000.00 in that currency.
- `POST /api/accounts/{id}/deposits` and `POST /api/accounts/{id}/withdrawals` with `amount` and `description`.
- `POST /api/accounts/{id}/close` closes an account.
- Each accepted command appends events: `AccountOpened`, `MoneyDeposited`, `MoneyWithdrawn`, `AccountClosed`.
- `GET /api/accounts/{id}/events` returns the raw stream, in order, with each event's version.

## Acceptance criteria
- Amounts must be positive and have at most two decimal places; otherwise `400`.
- A withdrawal larger than the balance is rejected with `422`; no overdraft.
- Withdrawals and outgoing transfers on the same UTC day cannot exceed the daily limit (`422`).
- A closed account accepts no deposits, withdrawals or transfers (`422`).
- An account can only be closed with balance 0 (`422`).
- An unknown account returns `404`.
- Concurrent commands on the same account never lose an event: the stream version is checked on append. A command that loses the race is re-run against the fresh state (up to 3 attempts), so its business rules are checked again.

## Out of scope
Interest, fees, overdraft, multiple owners, authentication.
