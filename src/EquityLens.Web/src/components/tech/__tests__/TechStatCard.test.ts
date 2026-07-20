import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import TechStatCard from '../TechStatCard.vue'

class MockIntersectionObserver {
  static instances: MockIntersectionObserver[] = []
  callback: IntersectionObserverCallback
  constructor(callback: IntersectionObserverCallback) {
    this.callback = callback
    MockIntersectionObserver.instances.push(this)
  }
  observe() {
    this.callback(
      [{ isIntersecting: true } as IntersectionObserverEntry],
      this as unknown as IntersectionObserver
    )
  }
  unobserve() {}
  disconnect() {}
}

describe('TechStatCard', () => {
  beforeEach(() => {
    MockIntersectionObserver.instances = []
    vi.stubGlobal('IntersectionObserver', MockIntersectionObserver)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('渲染 label、value 與 sub', () => {
    const wrapper = mount(TechStatCard, {
      props: { label: 'Sharpe Ratio', value: 1.42, decimals: 2, sub: '滾動 252 日' },
    })
    expect(wrapper.text()).toContain('Sharpe Ratio')
    expect(wrapper.text()).toContain('滾動 252 日')
    expect(wrapper.find('.tech-stat-value').exists()).toBe(true)
  })

  it('prefix/suffix 正確顯示', () => {
    const wrapper = mount(TechStatCard, {
      props: { label: 'Total Return', value: 18.72, prefix: '+', suffix: '%', decimals: 2 },
    })
    const valueText = wrapper.find('.tech-stat-value').text()
    expect(valueText).toContain('+')
    expect(valueText).toContain('%')
  })

  it('tone 屬性寫入 data-tone', () => {
    const wrapper = mount(TechStatCard, {
      props: { label: 'Max Drawdown', value: -8.4, suffix: '%', decimals: 1, tone: 'negative' },
    })
    expect(wrapper.attributes('data-tone')).toBe('negative')
  })
})
