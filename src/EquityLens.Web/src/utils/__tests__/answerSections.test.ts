import { describe, expect, it } from 'vitest'
import { splitAnswerSections } from '../answerSections'

// 正式站實際回覆（投組「測試1」近一年風險）。
const answer = `[P3] 近一年（2025-11-11 至 2026-11-11，241 個共同交易日）你的投組年化波動為 53.39%，最大回撤為 -31.66%；集中度 HHI 為 0.6449，最大單一持股權重 76.92%。[P4] 風險貢獻上，聯發科（2454）權重 76.92%、貢獻 91.59% 的組合風險，台積電（2330）權重 23.08%、貢獻 8.41%，風險明顯集中在聯發科。[P3]

| 指標 | 數值 |
|---|---|
| 年化波動 | 53.39% [P3] |

[P3] 波動採樣本標準差年化、日簡單報酬，權重為現有股數固定、歷史權重隨價格變動，不含帳戶現金；集中度與風險貢獻採期末市值固定權重。[P3] 依預設風險偏好「穩健」（尚未評估），年化波動 53.39% 高於產品參考上限 18.00%，為上限的 2.97 倍；最大回撤 -31.66% 低於參考下限 -25.00%。[P3]

工具唯讀，不代表持倉已調整；不保證獲利。

依預設的風險偏好『穩健』（尚未評估，可在設定中完成評估） [P4]。`

describe('splitAnswerSections', () => {
  it('keeps conclusion and table, folds method and thresholds, and moves fixed notices to the footer', () => {
    const sections = splitAnswerSections(answer)
    expect(sections.main).toContain('風險明顯集中在聯發科')
    expect(sections.main).toContain('| 年化波動 | 53.39% [P3] |')
    expect(sections.main).not.toContain('參考上限')
    expect(sections.main).not.toContain('樣本標準差')
    expect(sections.notes).toHaveLength(1)
    expect(sections.notes[0]).toContain('參考上限 18.00%')
    expect(sections.footer).toEqual(['工具唯讀，不代表持倉已調整；不保證獲利。', '依預設的風險偏好『穩健』（尚未評估，可在設定中完成評估） [P4]。'])
  })

  it('never hides the first paragraph and keeps every sentence somewhere', () => {
    const text = '限制：歷史資料不代表未來。\n\n結論段。\n\n限制：工具未提供 VaR。工具唯讀，不代表持倉已調整；不保證獲利。'
    const sections = splitAnswerSections(text)
    expect(sections.main).toBe('限制：歷史資料不代表未來。\n\n結論段。')
    expect(sections.notes).toEqual(['限制：工具未提供 VaR。'])
    expect(sections.footer).toEqual(['工具唯讀，不代表持倉已調整；不保證獲利。'])
  })

  it('deduplicates repeated notices that differ only by citation', () => {
    const text = '結論。\n\n依預設的風險偏好『穩健』（尚未評估） [P4]。\n\n依預設的風險偏好『穩健』（尚未評估） [P6]。'
    expect(splitAnswerSections(text).footer).toHaveLength(1)
  })
})
