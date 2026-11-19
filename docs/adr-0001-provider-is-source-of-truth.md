# ADR-0001: The payment provider is the source of truth for uncertain outcomes

- **Status:** Accepted
- **Date:** 2026-11-19

## Context

A charge request can time out after the provider has already charged the card. Our database
and the provider cannot share a transaction, so after a timeout we do not know what happened.

## Decision

1. Every charge carries the client's idempotency key, and we forward that same key to the provider.
2. A timeout marks the record `uncertain`. It is never treated as a failure.
3. Before any further charge attempt for an uncertain key, we query the provider by that key.
   - Charge found: store and return it.
   - No charge found: charge now, with the same key.
4. A background sweep resolves uncertain records proactively, but it only reads from the provider.
   It never starts a charge, because only a client retry expresses the intent to pay.
5. An expired in-progress lease (worker crash) is handled like `uncertain`.

## Consequences

- Duplicate charges are prevented even across crashes and lost responses.
- Clients can receive `202 Accepted` with status `Unknown`. They must retry with the same key or poll.
- This relies on the provider supporting idempotency keys and lookup by key. Stripe, Adyen and most
  modern PSPs do. For a provider that doesn't, store our own reference in the charge metadata and
  search by it.
