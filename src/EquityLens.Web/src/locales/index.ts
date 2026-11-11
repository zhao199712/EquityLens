import { createI18n } from 'vue-i18n'
import en from './en'
import zhTW from './zh-TW'

const STORAGE_KEY = 'equitylens-locale'
// 介面主要文案為繁體中文；未選擇語系的新訪客預設中文，避免建議追問與送出的問題混用英文。
const savedLocale = localStorage.getItem(STORAGE_KEY) || 'zh-TW'

const i18n = createI18n({
  legacy: false,
  locale: savedLocale,
  fallbackLocale: 'en',
  messages: { en, 'zh-TW': zhTW },
})

export function setLocale(locale: string) {
  ;(i18n.global.locale as { value: string }).value = locale
  localStorage.setItem(STORAGE_KEY, locale)
  document.documentElement.lang = locale === 'zh-TW' ? 'zh-TW' : 'en'
}

export default i18n
