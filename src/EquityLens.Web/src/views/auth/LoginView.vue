<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../../stores/auth'

const router = useRouter()
const authStore = useAuthStore()
const { t } = useI18n()

const email = ref('')
const password = ref('')
const loading = ref(false)
const error = ref('')

async function handleLogin() {
  if (!email.value || !password.value) {
    error.value = t('auth.login.errorFillAll')
    return
  }

  loading.value = true
  error.value = ''

  try {
    await authStore.login(email.value, password.value)
    router.push({ name: authStore.user?.role === 'Admin' ? 'admin-agent-runs' : 'dashboard' })
  } catch (e: any) {
    error.value = e?.response?.data?.message || t('auth.login.errorLoginFailed')
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="prestige-page auth-page">
    <div class="auth-container prestige-fade">
      <!-- Left Side - Brand -->
      <div class="auth-brand">
        <div class="auth-brand-content">
          <div class="auth-logo">
            <span class="auth-logo-mark">EL</span>
          </div>
          <h1 class="auth-title">EQUITYLENS</h1>
          <p class="auth-subtitle">AI-ASSISTED INVESTMENT ANALYTICS</p>
          <div class="auth-features">
            <div class="auth-feature-item">
              <div class="auth-feature-icon"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M4 20V12"/><path d="M10 20V5"/><path d="M16 20v-8"/><path d="M2.5 20h19"/></svg></div>
              <div>
                <div class="auth-feature-title">{{ t('auth.login.featurePortfolio') }}</div>
                <div class="auth-feature-desc">{{ t('auth.login.featurePortfolioDesc') }}</div>
              </div>
            </div>
            <div class="auth-feature-item">
              <div class="auth-feature-icon"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3l1.9 5.6 5.6 1.9-5.6 1.9L12 18l-1.9-5.6L4.5 10.5l5.6-1.9L12 3z"/><path d="M18.5 15.5l.9 2.6 2.6.9-2.6.9-.9 2.6-.9-2.6-2.6-.9 2.6-.9.9-2.6z"/></svg></div>
              <div>
                <div class="auth-feature-title">{{ t('auth.login.featureAI') }}</div>
                <div class="auth-feature-desc">{{ t('auth.login.featureAIDesc') }}</div>
              </div>
            </div>
            <div class="auth-feature-item">
              <div class="auth-feature-icon"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3l7 3v5c0 4.6-3 8.2-7 10-4-1.8-7-5.4-7-10V6l7-3z"/><path d="M9 11.5l2 2 4-4"/></svg></div>
              <div>
                <div class="auth-feature-title">{{ t('auth.login.featureRisk') }}</div>
                <div class="auth-feature-desc">{{ t('auth.login.featureRiskDesc') }}</div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Right Side - Login Form -->
      <div class="auth-form-wrapper">
        <div class="auth-form">
          <div class="auth-form-header">
            <span class="prestige-label">Member Access</span>
            <h2>{{ t('auth.login.welcomeBack') }}</h2>
            <p>{{ t('auth.login.pleaseLogin') }}</p>
          </div>

          <form class="auth-fields" @submit.prevent="handleLogin">
            <div class="auth-field">
              <label class="auth-field-label">{{ t('auth.login.email') }}</label>
              <input
                v-model="email"
                type="email"
                class="prestige-input"
                placeholder="your@email.com"
                autocomplete="email"
              />
            </div>

            <div class="auth-field">
              <label class="auth-field-label">{{ t('auth.login.password') }}</label>
              <input
                v-model="password"
                type="password"
                class="prestige-input"
                placeholder="••••••••"
                autocomplete="current-password"
              />
            </div>

            <div v-if="error" class="prestige-error">
              {{ error }}
            </div>

            <button
              type="submit"
              class="prestige-btn prestige-btn-solid auth-submit"
              :disabled="loading"
            >
              <span v-if="loading" class="auth-spinner"></span>
              <span v-else>{{ t('auth.login.loginBtn') }}</span>
            </button>
          </form>

          <div class="auth-form-footer">
            <p>{{ t('auth.login.noAccount') }} <RouterLink to="/register">{{ t('auth.login.register') }}</RouterLink></p>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.auth-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;
}

.auth-container {
  display: flex;
  width: 100%;
  max-width: 900px;
  min-height: 520px;
  background: var(--panel-bg);
  border: 1px solid var(--gold-border);
  border-radius: 6px;
  overflow: hidden;
}

.auth-brand {
  flex: 1;
  padding: 48px 40px;
  display: flex;
  align-items: center;
  border-right: 1px solid var(--gold-border-soft);
  background:
    repeating-radial-gradient(circle at 50% 0%, rgba(201, 168, 106, 0.03) 0 2px, transparent 2px 90px);
}

.auth-brand-content {
  width: 100%;
}

.auth-logo {
  margin-bottom: 24px;
}

.auth-logo-mark {
  display: inline-grid;
  width: 48px;
  height: 48px;
  place-items: center;
  border: 1px solid var(--gold-border);
  border-radius: 4px;
  color: var(--gold);
  font-family: var(--serif);
  font-size: 17px;
  font-weight: 700;
  letter-spacing: 0.05em;
}

.auth-title {
  font-family: var(--serif);
  font-size: 30px;
  font-weight: 600;
  letter-spacing: 0.08em;
  margin: 0 0 6px;
}

.auth-subtitle {
  font-size: 11px;
  letter-spacing: 0.24em;
  color: var(--gold);
  margin: 0 0 36px;
  text-transform: uppercase;
}

.auth-features {
  display: flex;
  flex-direction: column;
  gap: 0;
}

.auth-feature-item {
  display: flex;
  align-items: flex-start;
  gap: 14px;
  padding: 16px 0;
  border-bottom: 1px solid var(--gold-border-soft);
}

.auth-feature-item:first-child {
  border-top: 1px solid var(--gold-border-soft);
}

.auth-feature-icon {
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--gold-border-soft);
  border-radius: 4px;
  color: var(--gold);
  flex-shrink: 0;
}

.auth-feature-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--ivory);
}

.auth-feature-desc {
  font-size: 12px;
  color: var(--muted);
  margin-top: 2px;
  line-height: 1.6;
}

.auth-form-wrapper {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 48px 40px;
}

.auth-form {
  width: 100%;
  max-width: 320px;
}

.auth-form-header {
  margin-bottom: 32px;
}

.auth-form-header h2 {
  font-family: var(--serif);
  font-size: 24px;
  font-weight: 600;
  margin: 10px 0 4px;
}

.auth-form-header p {
  font-size: 13px;
  color: var(--muted);
  margin: 0;
}

.auth-fields {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.auth-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.auth-field-label {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--gold);
}

.auth-submit {
  width: 100%;
  justify-content: center;
  height: 46px;
}

.auth-spinner {
  width: 16px;
  height: 16px;
  border: 2px solid rgba(11, 18, 32, 0.3);
  border-top-color: #0b1220;
  border-radius: 50%;
  animation: auth-spin 0.6s linear infinite;
}

@keyframes auth-spin {
  to { transform: rotate(360deg); }
}

.auth-form-footer {
  margin-top: 24px;
  text-align: center;
}

.auth-form-footer p {
  font-size: 13px;
  color: var(--muted);
  margin: 0;
}

.auth-form-footer a {
  color: var(--gold);
  text-decoration: none;
  border-bottom: 1px solid var(--gold-border);
  transition: color 0.2s ease, border-color 0.2s ease;
}

.auth-form-footer a:hover {
  color: var(--gold-strong);
  border-color: var(--gold-strong);
}

@media (max-width: 768px) {
  .auth-container {
    flex-direction: column;
    max-width: 400px;
  }

  .auth-brand {
    border-right: none;
    border-bottom: 1px solid var(--gold-border-soft);
    padding: 32px 24px;
  }

  .auth-features {
    display: none;
  }

  .auth-form-wrapper {
    padding: 32px 24px;
  }
}
</style>
