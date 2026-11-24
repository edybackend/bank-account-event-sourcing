# 002 · Transfer between accounts

## Context
A transfer changes two aggregates. Both streams live in the same event store, so they are appended in one database transaction.

## Behavior
- `POST /api/transfers` with `fromAccountId`, `toAccountId`, `amount` and `description`.
- Appends `TransferSent` to the source stream and `TransferReceived` to the destination stream, both carrying the same `transferId`.

## Acceptance criteria
- Source and destination must be different accounts with the same currency (`422`).
- The source follows the same rules as a withdrawal (balance, daily limit, not closed); the destination must not be closed.
- Both events are stored or neither is: a failure on one stream leaves the other unchanged.
- The total money across all accounts is the same before and after any transfer.

## Out of scope
Currency conversion, scheduled transfers, transfers to other banks (that would be a saga across services).
