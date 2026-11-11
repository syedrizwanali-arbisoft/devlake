-- Adds the idempotency key that stops the same stats request being queued twice (INT-2: enforced by the database, not by an application check).
-- The key is a SHA-256 hex of client, event type, time frame and the sorted normalized emails. It is NULL for requests that must not block a
-- repeat (revoked, failed, or past the duplicate window); MySQL allows many NULLs in a unique index.
-- Purely additive. To roll back, drop the unique key and the column.

ALTER TABLE RequestLogs
  ADD COLUMN idempotencyKey VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
  ADD UNIQUE KEY UX_RequestLogs_idempotencyKey (idempotencyKey);
