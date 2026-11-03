import { test, expect } from '@playwright/test'
import { adminEmail, adminPassword } from './credentials'

test('admin flow: login, navigate admin pages, create import job', async ({ page }) => {
  test.setTimeout(60000)

  // 1. Login as admin
  await page.goto('/login')
  await page.waitForSelector('input[type="email"]', { timeout: 10000 })
  await page.fill('input[type="email"]', adminEmail)
  await page.fill('input[type="password"]', adminPassword())
  await page.click('button[type="submit"]')
  await page.waitForURL('/admin/agent-runs', { timeout: 15000 })

  // 2. Admin lands on agent runs
  await page.waitForSelector('text=Agent Run 管理', { timeout: 15000 })
  await expect(page.locator('text=Agent Run 管理')).toBeVisible()

  // 3. Navigate to admin research runs
  await page.goto('/admin/research-runs')
  await page.waitForSelector('text=Research Run 管理', { timeout: 15000 })
  await expect(page.locator('text=Research Run 管理')).toBeVisible()

  // 4. Navigate to admin jobs
  await page.goto('/admin/jobs')
  await page.waitForSelector('text=資料匯入工作管理', { timeout: 15000 })
  await expect(page.locator('text=資料匯入工作管理')).toBeVisible()

  // 5. Create a new ImportMarketPrices job
  await page.click('button:has-text("建立工作")')
  await page.waitForSelector('.n-modal:has-text("建立資料匯入工作")', { timeout: 10000 })
  await page.waitForTimeout(500)
  await page.fill('.n-form-item:has-text("Ticker") input', '2330')
  await page.locator('.n-modal:has-text("建立資料匯入工作") button:has-text("建立")').click({ force: true })

  // 6. Wait for job to appear in table
  await page.waitForSelector('text=ImportMarketPrices', { timeout: 15000 })
  await expect(page.locator('text=ImportMarketPrices').first()).toBeVisible()
})
