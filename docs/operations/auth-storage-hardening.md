# Authentication and file security hardening

Scope: JWT signing-key validation, digest-only refresh tokens, PDF uploads, externally reachable signed downloads, safe errors and frontend CSP. Email verification and Google account linking are deferred; the existing automatic-linking risk is not resolved by this release.

## Compatibility and deployment

- No EF model/schema migration. The existing `refresh_token.token` column stores `sha256:` followed by the lowercase SHA-256 digest of the random token. The bearer value is returned only to the client. There is no legacy plaintext lookup fallback.
- After every API replica is updated, run `scripts/security/retire-plaintext-refresh-tokens.sql` to revoke and scrub old plaintext credentials while retaining their audit rows. Users with old refresh tokens must log in again when refresh is needed. Existing access tokens retain their ordinary expiry. Historical backups may contain retired credentials and remain private.
- `Jwt:Key` must contain at least 32 UTF-8 bytes, generated cryptographically. Length validation cannot measure entropy. Existing rotated production keys remain unchanged.
- `ObjectStorage:ServiceUrl` remains the internal server endpoint. Set `ObjectStorage:PublicServiceUrl` to the external HTTPS origin. This release uses `https://equitylens.duckdns.org`, with the `equitylens` bucket path proxied to Garage by Caddy. If the bucket changes, update the explicit Caddy path matcher too.
- Review, validate and deploy `infra/caddy/Caddyfile` separately from the API deployment script. Caddy proxies only signed GET download requests for this bucket, without rewriting host/path/query; Garage verifies the signature. It does not expose S3 PUT, DELETE, or the admin API. Download responses use attachment/octet-stream, sandbox CSP and no-store. Signature/credential query values are redacted in access logs.
- Frontend `public/_headers` is applied by Cloudflare Pages after the frontend is built/deployed, not by Vite preview. CSP allows the production API, Google Identity Services and Google Fonts. Inline styles are retained for Vue/component libraries; inline scripts and eval are not allowed.
- `/api/files` and manual financial-filing uploads accept PDF only, at most 20 MiB, with extension/MIME/header and PDF parsing checks. Other formats now return 415. This is not malware scanning; downloads are forced attachments.
- Unexpected HTTP and chat-stream errors return generic messages. Server logs retain diagnostics; this does not claim that every persisted historical error record has been scrubbed.

## Verification

Tests cover weak signing keys, refresh digest non-replay and rotation, rejection of old plaintext tokens, disguised/truncated/oversized uploads, valid PDFs, cross-owner URL denial, external URL signing, invalid endpoint rejection and generic error redaction. The browser smoke suite applies the production CSP and checks normal login plus blocked inline-script injection. Live acceptance must verify signed file download and modified-signature rejection through Caddy, then remove test uploads and disable verification identities.

Existing redteam identities are disabled with `scripts/security/disable-redteam-accounts.sql`; preserve audit records. Do not claim email verification or Google-linking remediation from this release.
