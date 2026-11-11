/**
 * 把投組診斷答案拆成「重點」「計算方式與限制」「底部聲明」三部分，只改變呈現位置，不改寫任何文字或數字。
 * 第一段（結論）與表格一定留在重點；無法判斷的段落也留在重點。
 */
export interface AnswerSections {
  main: string
  notes: string[]
  footer: string[]
}

// 每次都附上的固定聲明：免責與風險偏好來源。
const footerPattern = /^(?:\[P\d+\]\s*)?(?:工具唯讀|依(?:預設的|你的|您的)風險偏好『)/
// 計算口徑、方法、限制與產品參考門檻比較；屬於「怎麼算」而不是「結果是什麼」。
const notesPattern = /^(?:\*\*)?限制[：:]|樣本標準差|年化[（(]?\s*252|日簡單報酬|歷史權重隨價格變動|現有股數固定|期末市值固定權重|不含帳戶現金|方法[：:]|計算口徑|參考上限|參考下限|產品參考/

const isTable = (paragraph: string) => /^\s*\|.*\|\s*$/m.test(paragraph) && /\|\s*:?-{3,}/.test(paragraph)

function splitFooterSentences(paragraph: string): { kept: string; footer: string[] } {
  // 「…分析值。工具唯讀，不代表持倉已調整；不保證獲利。」這類聲明可能接在同段最後一句。
  const match = /(工具唯讀[^。]*。?(?:不保證獲利。?)?)\s*$/.exec(paragraph)
  if (!match || match.index === 0) return { kept: paragraph, footer: [] }
  return { kept: paragraph.slice(0, match.index).trimEnd(), footer: [match[1]!.trim()] }
}

export function splitAnswerSections(markdown: string): AnswerSections {
  const paragraphs = markdown.replace(/\r\n?/g, '\n').split(/\n{2,}/).map(p => p.trim()).filter(Boolean)
  const main: string[] = []
  const notes: string[] = []
  const footer: string[] = []
  paragraphs.forEach((paragraph, index) => {
    if (index === 0 || isTable(paragraph)) { main.push(paragraph); return }
    if (footerPattern.test(paragraph)) { footer.push(paragraph); return }
    const { kept, footer: trailing } = splitFooterSentences(paragraph)
    footer.push(...trailing)
    if (!kept) return
    if (notesPattern.test(kept)) notes.push(kept)
    else main.push(kept)
  })
  const unique = [...new Map(footer.map(line => [line.replace(/\s*\[P\d+\]\s*/g, '').replace(/[。.\s]+$/, ''), line])).values()]
  return { main: main.join('\n\n'), notes, footer: unique }
}
