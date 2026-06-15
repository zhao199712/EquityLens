import { createApp } from 'vue'
import { createPinia } from 'pinia'
import naive from 'naive-ui'
import App from './App.vue'
import { router } from './router'
import i18n from './locales'
import './style.css'
import './assets/kimi-design.css'

createApp(App).use(createPinia()).use(router).use(i18n).use(naive).mount('#app')
