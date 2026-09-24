import { createApp } from 'vue'
import { createRouter, createWebHashHistory } from 'vue-router'
import App from './App.vue'
import './style.css'

import PortfolioView from './views/PortfolioView.vue'
import FinancialsView from './views/FinancialsView.vue'
import RiskView from './views/RiskView.vue'

const routes = [
  { path: '/', component: PortfolioView },
  { path: '/financials', component: FinancialsView },
  { path: '/risk', component: RiskView },
]

const router = createRouter({
  history: createWebHashHistory(),
  routes,
})

createApp(App).use(router).mount('#app')
