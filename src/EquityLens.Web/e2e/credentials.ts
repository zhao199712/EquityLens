export const adminEmail = 'playwright-admin-test@example.com'

export function adminPassword(): string {
  const password = process.env.E2E_ADMIN_PASSWORD
  if (!password || password.length < 32) throw new Error('E2E_ADMIN_PASSWORD is missing or too short.')
  return password
}
