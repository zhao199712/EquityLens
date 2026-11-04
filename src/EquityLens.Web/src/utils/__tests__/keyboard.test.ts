import { describe, expect, it } from 'vitest'
import { isSubmitEnter } from '../keyboard'

const key = (init: KeyboardEventInit & { keyCode?: number }) => {
  const event = new KeyboardEvent('keydown', init)
  if (init.keyCode !== undefined) Object.defineProperty(event, 'keyCode', { value: init.keyCode })
  return event
}

describe('isSubmitEnter', () => {
  it('accepts a plain Enter', () => {
    expect(isSubmitEnter(key({ key: 'Enter' }))).toBe(true)
  })

  it('ignores the Enter that confirms an IME candidate (Chrome/Firefox)', () => {
    expect(isSubmitEnter(key({ key: 'Enter', isComposing: true }))).toBe(false)
  })

  it('ignores the Enter that confirms an IME candidate (Safari keyCode 229)', () => {
    expect(isSubmitEnter(key({ key: 'Enter', keyCode: 229 }))).toBe(false)
  })

  it('treats Shift+Enter as a newline unless allowed', () => {
    expect(isSubmitEnter(key({ key: 'Enter', shiftKey: true }))).toBe(false)
    expect(isSubmitEnter(key({ key: 'Enter', shiftKey: true }), { allowShift: true })).toBe(true)
  })

  it('ignores other keys', () => {
    expect(isSubmitEnter(key({ key: 'a' }))).toBe(false)
  })
})
