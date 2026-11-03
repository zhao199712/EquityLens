import { readFileSync } from 'node:fs'
import { test, expect, login } from './fixtures'

const headers = readFileSync('public/_headers', 'utf8')
const policy = headers.split('\n').find(line => line.trimStart().startsWith('Content-Security-Policy:'))!.split('Content-Security-Policy:')[1]!.trim()

test('production CSP permits the app and blocks inline script injection', async ({ page }) => {
  // Vite preview does not apply Cloudflare _headers. Apply the actual policy to document responses.
  await page.route('http://127.0.0.1:5186/**', async route => {
    if (route.request().resourceType() !== 'document') return route.fallback()
    const response = await route.fetch()
    await route.fulfill({ response, headers: { ...response.headers(), 'content-security-policy': policy } })
  })
  await login(page)
  const googleLoaded = await page.evaluate(() => new Promise<boolean>(resolve => {
    const script = document.createElement('script')
    script.src = 'https://accounts.google.com/gsi/client'
    script.onload = () => resolve(true)
    script.onerror = () => resolve(false)
    document.head.appendChild(script)
  }))
  expect(googleLoaded).toBe(true)
  const blocked = await page.evaluate(async () => {
    const result = new Promise<boolean>(resolve => {
      document.addEventListener('securitypolicyviolation', event => {
        if (event.violatedDirective === 'script-src-elem') resolve(true)
      }, { once: true })
      setTimeout(() => resolve(false), 2000)
    })
    const script = document.createElement('script')
    script.textContent = 'document.documentElement.dataset.injected = "yes"'
    document.body.appendChild(script)
    return await result
  })
  expect(blocked).toBe(true)
  expect(await page.locator('html').getAttribute('data-injected')).toBeNull()
  await expect(page.getByRole('heading', { name: '投資組合', exact: true })).toBeVisible()
})
