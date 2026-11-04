/**
 * 判斷 keydown 是否為「送出用」的 Enter。
 *
 * 使用中文、日文等輸入法時，選字確認的 Enter 也會觸發鍵盤事件：
 * Chrome／Firefox 在組字中會帶 `isComposing = true`，Safari 則以 `keyCode 229` 表示。
 * 這些 Enter 只是確認候選字，不可當成送出。
 * 必須搭配 keydown 使用；keyup 在選字結束後才觸發，已無法分辨。
 */
export function isSubmitEnter(event: KeyboardEvent, options: { allowShift?: boolean } = {}): boolean {
  if (event.key !== 'Enter') return false
  if (event.isComposing || event.keyCode === 229) return false
  if (event.shiftKey && !options.allowShift) return false
  return true
}
