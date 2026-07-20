import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import LoginView from '../views/auth/LoginView.vue'
import RegisterView from '../views/auth/RegisterView.vue'
import DashboardView from '../views/DashboardView.vue'
import HomeView from '../views/home/HomeView.vue'
import StyleLabView from '../views/home-lab/StyleLabView.vue'
import AuroraHomeView from '../views/home-lab/AuroraHomeView.vue'
import SwissHomeView from '../views/home-lab/SwissHomeView.vue'
import PrestigeHomeView from '../views/home-lab/PrestigeHomeView.vue'
import AnalystHomeView from '../views/home-lab/AnalystHomeView.vue'
import ZenHomeView from '../views/home-lab/ZenHomeView.vue'
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
import WorkflowManagementView from '../views/admin/WorkflowManagementView.vue'
import NodeCatalogManagementView from '../views/admin/NodeCatalogManagementView.vue'
import AgentRunListView from '../views/agent-runs/AgentRunListView.vue'
import AgentRunDetailView from '../views/agent-runs/AgentRunDetailView.vue'
import ResearchRunListView from '../views/research/ResearchRunListView.vue'
import ResearchRunDetailView from '../views/research/ResearchRunDetailView.vue'
import AgentRunManagementView from '../views/admin/AgentRunManagementView.vue'
import ResearchRunManagementView from '../views/admin/ResearchRunManagementView.vue'

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
      path: '/home',
      name: 'home-neon',
      component: HomeView,
      meta: { requiresAuth: false },
    },
    {
      path: '/home/lab',
      name: 'home-lab',
      component: StyleLabView,
      meta: { requiresAuth: false },
    },
    {
      path: '/home/aurora',
      name: 'home-aurora',
      component: AuroraHomeView,
      meta: { requiresAuth: false },
    },
    {
      path: '/home/swiss',
      name: 'home-swiss',
      component: SwissHomeView,
      meta: { requiresAuth: false },
    },
    {
      path: '/home/prestige',
      name: 'home-prestige',
      component: PrestigeHomeView,
      meta: { requiresAuth: false },
    },
    {
      path: '/home/analyst',
      name: 'home-analyst',
      component: AnalystHomeView,
      meta: { requiresAuth: false },
    },
    {
      path: '/home/zen',
      name: 'home-zen',
      component: ZenHomeView,
      meta: { requiresAuth: false },
    },
    {
      path: '/',
      name: 'home',
      component: PrestigeHomeView,
      meta: { requiresAuth: false },
    },
    {
      path: '/dashboard',
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
      path: '/research',
      name: 'research',
      component: ResearchRunListView,
    },
    {
      path: '/research/:id',
      name: 'research-run-detail',
      component: ResearchRunDetailView,
    },
    {
      path: '/settings',
      name: 'settings',
      component: SettingsView,
    },
    {
      path: '/admin',
      component: AdminLayout,
      meta: { requiresAuth: true, requiresAdmin: true },
      redirect: '/admin/agent-runs',
      children: [
        { path: 'workflows', name: 'admin-workflows', component: WorkflowManagementView },
        { path: 'nodes', name: 'admin-nodes', component: NodeCatalogManagementView },
        {
          path: 'agent-runs',
          name: 'admin-agent-runs',
          component: AgentRunManagementView,
        },
        {
          path: 'agent-runs/:id',
          name: 'admin-agent-run-detail',
          component: AgentRunManagementView,
        },
        {
          path: 'research-runs',
          name: 'admin-research-runs',
          component: ResearchRunManagementView,
        },
        {
          path: 'jobs',
          name: 'admin-jobs',
          component: JobManagementView,
        },
        {
          path: 'jobs/:id',
          name: 'admin-job-detail',
          component: JobManagementView,
        },
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
      ],
    },
  ],
})

router.beforeEach(async (to) => {
  const authStore = useAuthStore()

  if (to.meta.requiresAuth !== false && !authStore.isAuthenticated) {
    return { name: 'login' }
  }

  if (authStore.isAuthenticated && !authStore.user) {
    await authStore.fetchUser()
  }

  if (to.meta.requiresAuth !== false && !authStore.isAuthenticated) {
    return { name: 'login' }
  }

  if ((to.name === 'login' || to.name === 'register') && authStore.isAuthenticated) {
    return { name: 'dashboard' }
  }
  if (to.meta.requiresAdmin && authStore.user?.role !== 'Admin') {
    return { name: 'dashboard' }
  }
})
