import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import CapabilityRequestStatus from '../CapabilityRequestStatus.vue'

describe('CapabilityRequestStatus', () => {
  it('shows a pending Web capability proposal', () => {
    const wrapper = mount(CapabilityRequestStatus, {
      props: {
        assessment: {
          decision: 'Request',
          reason: '缺少前一季法說會基線。',
          evidenceGaps: ['前一季指引'],
          confidence: 'high',
          mode: 'Llm',
        },
        requests: [{
          requestId: 'request-1',
          capabilityId: 'retrieve-web-research-evidence',
          status: 'Pending',
          reason: '缺少前一季法說會基線。',
          reviewReason: null,
        }],
      },
    })

    expect(wrapper.text()).toContain('Web Search：建議使用')
    expect(wrapper.text()).toContain('retrieve-web-research-evidence')
    expect(wrapper.text()).toContain('等待 Planner 審核')
  })

  it('shows the Planner approval reason', () => {
    const wrapper = mount(CapabilityRequestStatus, {
      props: {
        assessment: { decision: 'Request', reason: '需要外部證據。', confidence: 'high', mode: 'Llm' },
        requests: [{
          requestId: 'request-1',
          capabilityId: 'retrieve-web-research-evidence',
          status: 'Approved',
          reason: '需要外部證據。',
          reviewReason: 'Planner 核准 Web 檢索。',
        }],
      },
    })

    expect(wrapper.text()).toContain('已核准')
    expect(wrapper.text()).toContain('Planner 核准 Web 檢索')
  })
})
