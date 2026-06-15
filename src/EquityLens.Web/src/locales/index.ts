import { createI18n } from 'vue-i18n'
import en from './en'
import zhTW from './zh-TW'

const STORAGE_KEY = 'equitylens-locale'
const savedLocale = localStorage.getItem(STORAGE_KEY) || 'en'

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
