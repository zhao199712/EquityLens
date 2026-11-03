-- Preserve audit records; JWT validation rejects these identities immediately.
BEGIN;
UPDATE app_user SET is_active = false, password_hash = NULL, updated_at_utc = now()
WHERE email IN ('redteam-a@test.example', 'redteam-b@test.example');
UPDATE refresh_token SET is_revoked = true
WHERE user_id IN (
    SELECT id FROM app_user WHERE email IN ('redteam-a@test.example', 'redteam-b@test.example')
);
COMMIT;
