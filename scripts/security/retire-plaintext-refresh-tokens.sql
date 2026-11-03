-- Run after all API replicas use digest-only refresh lookup. No schema changes.
-- Preserve audit rows while removing bearer credentials. Safe to run again.
BEGIN;
UPDATE refresh_token
SET is_revoked = true, token = 'retired:' || id::text
WHERE token !~ '^sha256:[0-9a-f]{64}$'
  AND token NOT LIKE 'retired:%';
COMMIT;
SELECT count(*) AS remaining_plaintext_refresh_tokens
FROM refresh_token
WHERE token !~ '^sha256:[0-9a-f]{64}$'
  AND token NOT LIKE 'retired:%';
