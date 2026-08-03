import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'

const LoginView = () => import('../views/auth/LoginView.vue')
const RegisterView = () => import('../views/auth/RegisterView.vue')
const DashboardView = () => import('../views/DashboardView.vue')
const HomeView = () => import('../views/home/HomeView.vue')
const StyleLabView = () => import('../views/home-lab/StyleLabView.vue')
const AuroraHomeView = () => import('../views/home-lab/AuroraHomeView.vue')
const SwissHomeView = () => import('../views/home-lab/SwissHomeView.vue')
const PrestigeHomeView = () => import('../views/home-lab/PrestigeHomeView.vue')
const AnalystHomeView = () => import('../views/home-lab/AnalystHomeView.vue')
const ZenHomeView = () => import('../views/home-lab/ZenHomeView.vue')
const PortfolioListView = () => import('../views/portfolios/PortfolioListView.vue')
const PortfolioDetailView = () => import('../views/portfolios/PortfolioDetailView.vue')
const RiskRunListView = () => import('../views/risk/RiskRunListView.vue')
const RiskRunDetailView = () => import('../views/risk/RiskRunDetailView.vue')
const FinancialReportListView = () => import('../views/reports/FinancialReportListView.vue')
const FinancialReportDetailView = () => import('../views/reports/FinancialReportDetailView.vue')
const SettingsView = () => import('../views/settings/SettingsView.vue')
const AdminLayout = () => import('../views/admin/AdminLayout.vue')
const UserManagementView = () => import('../views/admin/UserManagementView.vue')
const StockManagementView = () => import('../views/admin/StockManagementView.vue')
const JobManagementView = () => import('../views/admin/JobManagementView.vue')
const PriceManagementView = () => import('../views/admin/PriceManagementView.vue')
const ReportManagementView = () => import('../views/admin/ReportManagementView.vue')
const RiskModelManagementView = () => import('../views/admin/RiskModelManagementView.vue')
const AISettingsView = () => import('../views/admin/AISettingsView.vue')
const WorkflowManagementView = () => import('../views/admin/WorkflowManagementView.vue')
const NodeCatalogManagementView = () => import('../views/admin/NodeCatalogManagementView.vue')
const AgentRegistryView = () => import('../views/admin/AgentRegistryView.vue')
const PromptManagementView = () => import('../views/admin/PromptManagementView.vue')
const AgentRunListView = () => import('../views/agent-runs/AgentRunListView.vue')
const AgentRunDetailView = () => import('../views/agent-runs/AgentRunDetailView.vue')
const AgentQueryView = () => import('../views/agent-runs/AgentQueryView.vue')
const ResearchRunListView = () => import('../views/research/ResearchRunListView.vue')
const ResearchRunDetailView = () => import('../views/research/ResearchRunDetailView.vue')
const AgentRunManagementView = () => import('../views/admin/AgentRunManagementView.vue')
const ResearchRunManagementView = () => import('../views/admin/ResearchRunManagementView.vue')

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
      path: '/ask-agent',
      name: 'ask-agent',
      component: AgentQueryView,
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
        { path: 'registry', name: 'admin-registry', component: AgentRegistryView },
        { path: 'prompts', name: 'admin-prompts', component: PromptManagementView },
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
