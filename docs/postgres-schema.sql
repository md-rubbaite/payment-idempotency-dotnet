-- Production schema for the idempotency store (PostgreSQL).
-- The UNIQUE primary key is what makes TryBegin atomic under concurrency.

CREATE TABLE idempotency_keys (
    idempotency_key     varchar(255) PRIMARY KEY,
    request_fingerprint char(64)     NOT NULL,
    state               varchar(16)  NOT NULL CHECK (state IN ('in_progress', 'completed', 'uncertain')),
    result              jsonb,
    created_at          timestamptz  NOT NULL DEFAULT now(),
    lease_expires_at    timestamptz  NOT NULL
);

CREATE INDEX ix_idempotency_uncertain ON idempotency_keys (created_at) WHERE state = 'uncertain';

-- TryBegin: claim the key in one statement. Zero rows inserted means the key exists,
-- so read the row and decide (replay / conflict / mismatch / reconcile).
INSERT INTO idempotency_keys (idempotency_key, request_fingerprint, state, lease_expires_at)
VALUES (@key, @fingerprint, 'in_progress', now() + interval '30 seconds')
ON CONFLICT (idempotency_key) DO NOTHING;

-- Retention: keys must outlive the longest client retry window (24-72 hours is common).
DELETE FROM idempotency_keys WHERE created_at < now() - interval '7 days' AND state = 'completed';
