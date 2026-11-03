-- Run only against the explicitly selected affected database.
-- Retain the account and its audit records; revoke credentials and privileges.
-- Existing access JWTs require the patched API (or signing-key rotation).
BEGIN;

UPDATE app_user
SET is_active = false,
    role = 'User',
    password_hash = NULL,
    updated_at_utc = now()
WHERE lower(btrim(email)) = 'playwright-admin-test@example.com';

UPDATE refresh_token
SET is_revoked = true
WHERE user_id IN (
    SELECT id FROM app_user
    WHERE lower(btrim(email)) = 'playwright-admin-test@example.com'
);

COMMIT;
