import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import LoginView from '../views/auth/LoginView.vue'
import RegisterView from '../views/auth/RegisterView.vue'
import DashboardView from '../views/DashboardView.vue'
import PortfolioListView from '../views/portfolios/PortfolioListView.vue'
import PortfolioDetailView from '../views/portfolios/PortfolioDetailView.vue'
import RiskRunListView from '../views/risk/RiskRunListView.vue'
import RiskRunDetailView from '../views/risk/RiskRunDetailView.vue'
import FinancialReportListView from '../views/reports/FinancialReportListView.vue'
import FinancialReportDetailView from '../views/reports/FinancialReportDetailView.vue'
import SettingsView from '../views/settings/SettingsView.vue'
import AdminLayout from '../views/admin/AdminLayout.vue'
import UserManagementView from '../views/admin/UserManagementView.vue'
import StockManagementView from '../views/admin/StockManagementView.vue'
import JobManagementView from '../views/admin/JobManagementView.vue'
import PriceManagementView from '../views/admin/PriceManagementView.vue'
import ReportManagementView from '../views/admin/ReportManagementView.vue'
import RiskModelManagementView from '../views/admin/RiskModelManagementView.vue'
import AISettingsView from '../views/admin/AISettingsView.vue'
import AgentRunListView from '../views/agent-runs/AgentRunListView.vue'
import AgentRunDetailView from '../views/agent-runs/AgentRunDetailView.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: LoginView,
      meta: { requiresAuth: false },
    },
    {
      path: '/register',
      name: 'register',
      component: RegisterView,
      meta: { requiresAuth: false },
    },
    {
      path: '/',
      name: 'dashboard',
      component: DashboardView,
      meta: { requiresAuth: true },
    },
    {
      path: '/portfolios',
      name: 'portfolios',
      component: PortfolioListView,
    },
    {
      path: '/portfolios/:id',
      name: 'portfolio-detail',
      component: PortfolioDetailView,
    },
    {
      path: '/risk-runs',
      name: 'risk-runs',
      component: RiskRunListView,
    },
    {
      path: '/risk-runs/:id',
      name: 'risk-run-detail',
      component: RiskRunDetailView,
    },
    {
      path: '/financial-reports',
      name: 'financial-reports',
      component: FinancialReportListView,
    },
    {
      path: '/financial-reports/:id',
      name: 'financial-report-detail',
      component: FinancialReportDetailView,
    },
    {
      path: '/agent-runs',
      name: 'agent-runs',
      component: AgentRunListView,
    },
    {
      path: '/agent-runs/:id',
      name: 'agent-run-detail',
      component: AgentRunDetailView,
    },
    {
      path: '/settings',
      name: 'settings',
      component: SettingsView,
    },
    {
      path: '/admin',
      component: AdminLayout,
      redirect: '/admin/users',
      children: [
        {
          path: 'users',
          name: 'admin-users',
          component: UserManagementView,
        },
        {
          path: 'stocks',
          name: 'admin-stocks',
          component: StockManagementView,
        },
        {
          path: 'prices',
          name: 'admin-prices',
          component: PriceManagementView,
        },
        {
          path: 'reports',
          name: 'admin-reports',
          component: ReportManagementView,
        },
        {
          path: 'risk-models',
          name: 'admin-risk-models',
          component: RiskModelManagementView,
        },
        {
          path: 'ai-settings',
          name: 'admin-ai-settings',
          component: AISettingsView,
        },
        {
          path: 'jobs',
          name: 'admin-jobs',
          component: JobManagementView,
        },
      ],
    },
  ],
})

router.beforeEach((to) => {
  const authStore = useAuthStore()

  if (to.meta.requiresAuth !== false && !authStore.isAuthenticated) {
    return { name: 'login' }
  }
  if ((to.name === 'login' || to.name === 'register') && authStore.isAuthenticated) {
    return { name: 'dashboard' }
  }
})
