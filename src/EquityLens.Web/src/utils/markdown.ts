/**
 * 對話與報告用的輕量 markdown 渲染。
 * 先 escape 全部 HTML，再只產生固定的標籤（表格、標題、清單、段落、粗體、斜體、行內 code），
 * 因此模型輸出無法注入任意 HTML。引用標記 [P1]／[1] 轉為小型標籤。
 */
function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

function inline(text: string): string {
  return escapeHtml(text)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/(^|[^*])\*([^*\s][^*]*?)\*(?!\*)/g, '$1<em>$2</em>')
    .replace(/\[(P?\d+)\]/g, '<sup class="md-cite">[$1]</sup>')
}

const tableRow = /^\s*\|.*\|\s*$/
const tableDivider = /^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$/

function cells(row: string): string[] {
  return row.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(cell => cell.trim())
}

function renderTable(lines: string[]): string {
  const header = cells(lines[0]!)
  const align = cells(lines[1]!).map(cell => (cell.endsWith(':') ? (cell.startsWith(':') ? 'center' : 'right') : ''))
  const attr = (index: number) => (align[index] ? ` style="text-align:${align[index]}"` : '')
  const head = header.map((cell, index) => `<th${attr(index)}>${inline(cell)}</th>`).join('')
  const body = lines.slice(2)
    .map(row => `<tr>${cells(row).map((cell, index) => `<td${attr(index)}>${inline(cell)}</td>`).join('')}</tr>`)
    .join('')
  return `<div class="md-table-wrap"><table class="md-table"><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table></div>`
}

export function renderMarkdown(text: string): string {
  if (!text) return ''
  const lines = text.replace(/\r\n?/g, '\n').split('\n')
  const blocks: string[] = []
  let paragraph: string[] = []
  const flush = () => {
    if (paragraph.length) blocks.push(`<p>${paragraph.map(inline).join('<br>')}</p>`)
    paragraph = []
  }
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]!
    if (tableRow.test(line) && i + 1 < lines.length && tableDivider.test(lines[i + 1]!)) {
      flush()
      const table = [line, lines[i + 1]!]
      i += 2
      while (i < lines.length && tableRow.test(lines[i]!)) table.push(lines[i++]!)
      i--
      blocks.push(renderTable(table))
      continue
    }
    const heading = /^\s*(#{1,4})\s+(.+)$/.exec(line)
    if (heading) {
      flush()
      const level = Math.min(heading[1]!.length + 2, 6)
      blocks.push(`<h${level} class="md-heading">${inline(heading[2]!)}</h${level}>`)
      continue
    }
    const bullet = /^\s*(?:[-*•]|\d+[.)])\s+/
    if (bullet.test(line)) {
      flush()
      const ordered = /^\s*\d+[.)]\s+/.test(line)
      const items: string[] = []
      while (i < lines.length && bullet.test(lines[i]!)) items.push(`<li>${inline(lines[i++]!.replace(bullet, ''))}</li>`)
      i--
      blocks.push(ordered ? `<ol>${items.join('')}</ol>` : `<ul>${items.join('')}</ul>`)
      continue
    }
    if (!line.trim()) { flush(); continue }
    paragraph.push(line)
  }
  flush()
  return blocks.join('')
}
