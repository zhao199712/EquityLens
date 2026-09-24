import { expect, test, type Page } from '@playwright/test'

const apiBaseUrl = 'http://127.0.0.1:5036/api'
const nodeType = 'E2EExecuteDeterministicSideEffect'

type NodeSetting = { nodeType: string; isEnabled: boolean; displayName: string; description: string; timeoutSeconds: number; maxRetryCount: number; requiresHumanApprovalOverride: boolean | null; metadata: Record<string, unknown> | null }

async function login(page: Page) {
  await page.goto('/login')
  await page.locator('input[type="email"]').fill('playwright-admin-test@example.com')
  await page.locator('input[type="password"]').fill('Test1234!')
  await page.locator('button[type="submit"]').click()
  await page.waitForURL('/admin/agent-runs')
}

async function api(page: Page, path: string, method = 'GET', data?: unknown) {
  const accessToken = await page.evaluate(() => sessionStorage.getItem('auth_token'))
  expect(accessToken).toBeTruthy()
  const response = await page.request.fetch(`${apiBaseUrl}${path}`, { method, data, headers: { Authorization: `Bearer ${accessToken}` } })
  expect(response.ok(), `${method} ${path} should succeed`).toBeTruthy()
  if (response.status() === 204) return null
  return response.json()
}

async function waitForRun(page: Page, id: string, predicate: (detail: any) => boolean) {
  await expect.poll(async () => predicate(await api(page, `/agent-runs/${id}`)), { timeout: 60_000 }).toBe(true)
  return api(page, `/agent-runs/${id}`)
}

test.describe.configure({ mode: 'serial' })

test.describe('Human approval gate', () => {
  let original: NodeSetting

  test.beforeEach(async ({ page }) => {
    await login(page)
    const nodes = await api(page, '/admin/agent-nodes') as NodeSetting[]
    original = nodes.find(node => node.nodeType === nodeType)!
    expect(original).toBeTruthy()
    await page.goto('/admin/nodes')
    const row = page.locator('tr').filter({ hasText: nodeType })
    await row.getByRole('button', { name: '設定' }).click()
    await page.locator('.n-modal').getByLabel('Human Approval Gate').selectOption('true')
    await page.locator('.n-modal').getByRole('button', { name: '儲存有效設定' }).click()
    await expect(page.getByText('Node 設定已儲存')).toBeVisible()
  })

  test.afterEach(async ({ page }) => {
    if (original) await api(page, `/admin/agent-nodes/${nodeType}`, 'PUT', original)
  })

  const create = (page: Page) => api(page, '/e2e/approval-gate-runs', 'POST')

  test('approve resumes the deterministic handler and completes the workflow', async ({ page }) => {
    const approved = await create(page)
    const waiting = await waitForRun(page, approved.agentRunId, detail => detail.run.status === 'WaitingForApproval' && detail.approvals?.some((item: any) => item.status === 'Pending'))
    expect(waiting.nodes.find((node: any) => node.nodeType === nodeType)?.startedAtUtc).toBeNull()
    await page.goto(`/admin/agent-runs/${approved.agentRunId}`)
    await expect(page.getByTestId('admin-approval-panel')).toBeVisible()
    await page.getByRole('button', { name: '批准並繼續' }).click()
    await expect(page.getByText('已批准並排程恢復。')).toBeVisible()
    await api(page, `/e2e/approval-gate-runs/${approved.agentRunId}/execute`, 'POST')
    const completed = await waitForRun(page, approved.agentRunId, detail =>
      detail.run.status === 'Succeeded' && Boolean(detail.approvals?.[0]?.consumedAtUtc))
    expect(completed.approvals[0].status).toBe('Approved')
    expect(completed.nodes.find((node: any) => node.nodeType === nodeType)?.status).toBe('Succeeded')
    expect(JSON.parse(completed.outputJson).handlerExecuted).toBe(true)
  })

  test('reject cancels the run without executing the deterministic handler', async ({ page }) => {
    const rejected = await create(page)
    await waitForRun(page, rejected.agentRunId, detail => detail.run.status === 'WaitingForApproval')
    await page.goto(`/admin/agent-runs/${rejected.agentRunId}`)
    await page.getByPlaceholder('批准可選填；拒絕必填').fill('E2E reject verification')
    await page.getByRole('button', { name: '拒絕並取消' }).click()
    await expect(page.getByText('已拒絕並取消 Run。')).toBeVisible()
    const cancelled = await waitForRun(page, rejected.agentRunId, detail => detail.run.status === 'Cancelled')
    expect(cancelled.approvals[0].status).toBe('Rejected')
    const node = cancelled.nodes.find((item: any) => item.nodeType === nodeType)
    expect(node.status).toBe('Cancelled')
    expect(node.startedAtUtc).toBeNull()
    expect(cancelled.outputJson).toBeNull()
  })
})
