import { describe, expect, it } from 'vitest'
import { renderMarkdown } from '../markdown'

describe('renderMarkdown', () => {
  it('renders GFM tables with alignment', () => {
    const html = renderMarkdown('| 持股 | 權重 |\n|---|---:|\n| 台光電（2383） | 54.17% [P3] |')
    expect(html).toContain('<table class="md-table">')
    expect(html).toContain('<th style="text-align:right">權重</th>')
    expect(html).toContain('<td>台光電（2383）</td>')
    expect(html).toContain('<sup class="md-cite">[P3]</sup>')
    expect(html).not.toContain('|---')
  })

  it('escapes HTML from model output', () => {
    const html = renderMarkdown('<img src=x onerror=alert(1)> **粗體**')
    expect(html).not.toContain('<img')
    expect(html).toContain('&lt;img')
    expect(html).toContain('<strong>粗體</strong>')
  })

  it('renders lists, headings and paragraphs', () => {
    const html = renderMarkdown('## 結論\n- 第一點\n- 第二點\n\n段落一\n段落二')
    expect(html).toContain('<h4 class="md-heading">結論</h4>')
    expect(html).toContain('<ul><li>第一點</li><li>第二點</li></ul>')
    expect(html).toContain('<p>段落一<br>段落二</p>')
  })

  it('keeps negative numbers and multiplication-like asterisks intact', () => {
    expect(renderMarkdown('VaR -5.65%，2 * 3')).toBe('<p>VaR -5.65%，2 * 3</p>')
  })
})
