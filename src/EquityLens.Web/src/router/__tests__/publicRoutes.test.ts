import { beforeEach, afterAll, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
vi.mock('../../services/http', () => ({ http: { defaults: { headers: { common: {} } } } }))
vi.stubEnv('DEV', false)
beforeEach(() => { sessionStorage.clear(); setActivePinia(createPinia()) })
afterAll(() => vi.unstubAllEnvs())

it('uses the real router to show a public 404 without redirecting to login', async () => {
  const { router } = await import('../index')
  await router.push('/missing-integration-page')
  expect(router.currentRoute.value.name).toBe('not-found')
  expect(router.currentRoute.value.meta.requiresAuth).toBe(false)
})
it('does not register style labs or retired admin catalogs in production', async () => {
  const { router } = await import('../index')
  expect(router.resolve('/home/lab').name).toBe('not-found')
  expect(router.resolve('/admin/capabilities').name).toBe('not-found')
  expect(router.hasRoute('research')).toBe(true)
  expect(router.hasRoute('agent-runs')).toBe(true)
})
