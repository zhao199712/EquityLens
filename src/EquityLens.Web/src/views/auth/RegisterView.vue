<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../../stores/auth'

const router = useRouter()
const authStore = useAuthStore()

const displayName = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const loading = ref(false)
const error = ref('')

async function handleRegister() {
  if (!email.value || !password.value) {
    error.value = '請填寫所有欄位'
    return
  }

  if (password.value.length < 6) {
    error.value = '密碼至少需要 6 個字元'
    return
  }

  if (password.value !== confirmPassword.value) {
    error.value = '兩次密碼輸入不一致'
    return
  }

  loading.value = true
  error.value = ''

  try {
    await authStore.register(email.value, password.value, displayName.value || undefined)
    router.push({ name: 'dashboard' })
  } catch (e: any) {
    const msg = e?.response?.data?.message
    if (e?.response?.status === 409) {
      error.value = '此 Email 已被註冊'
    } else {
      error.value = msg || '註冊失敗，請稍後再試'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="kimi-login-page">
    <div class="kimi-login-container">
      <!-- Left Side - Brand -->
      <div class="kimi-login-brand">
        <div class="kimi-login-brand-content">
          <div class="kimi-login-logo">
            <span class="kimi-brand-mark">EL</span>
          </div>
          <h1 class="kimi-login-title">EQUITYLENS</h1>
          <p class="kimi-login-subtitle">AI-ASSISTED INVESTMENT ANALYTICS</p>
          <div class="kimi-login-features">
            <div class="kimi-feature-item">
              <div class="kimi-feature-icon">📊</div>
              <div>
                <div class="kimi-feature-title">Portfolio Tracking</div>
                <div class="kimi-feature-desc">即時追蹤投資組合表現</div>
              </div>
            </div>
            <div class="kimi-feature-item">
              <div class="kimi-feature-icon">🤖</div>
              <div>
                <div class="kimi-feature-title">AI Analysis</div>
                <div class="kimi-feature-desc">智能風險與財報分析</div>
              </div>
            </div>
            <div class="kimi-feature-item">
              <div class="kimi-feature-icon">📈</div>
              <div>
                <div class="kimi-feature-title">Risk Management</div>
                <div class="kimi-feature-desc">數據驅動的資產配置</div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Right Side - Register Form -->
      <div class="kimi-login-form-wrapper">
        <div class="kimi-login-form">
          <div class="kimi-login-header">
            <h2>建立帳號</h2>
            <p>填寫以下資訊完成註冊</p>
          </div>

          <form class="kimi-form" @submit.prevent="handleRegister">
            <div class="kimi-form-group">
              <label class="kimi-label">顯示名稱</label>
              <input
                v-model="displayName"
                type="text"
                class="kimi-input"
                placeholder="選填"
                autocomplete="name"
              />
            </div>

            <div class="kimi-form-group">
              <label class="kimi-label">電子信箱</label>
              <input
                v-model="email"
                type="email"
                class="kimi-input"
                placeholder="your@email.com"
                autocomplete="email"
              />
            </div>

            <div class="kimi-form-group">
              <label class="kimi-label">密碼</label>
              <input
                v-model="password"
                type="password"
                class="kimi-input"
                placeholder="至少 6 個字元"
                autocomplete="new-password"
              />
            </div>

            <div class="kimi-form-group">
              <label class="kimi-label">確認密碼</label>
              <input
                v-model="confirmPassword"
                type="password"
                class="kimi-input"
                placeholder="再次輸入密碼"
                autocomplete="new-password"
              />
            </div>

            <div v-if="error" class="kimi-error">
              {{ error }}
            </div>

            <button
              type="submit"
              class="kimi-btn kimi-btn-primary kimi-btn-full"
              :disabled="loading"
            >
              <span v-if="loading" class="kimi-spinner"></span>
              <span v-else>註冊</span>
            </button>
          </form>

          <div class="kimi-login-footer">
            <p>已經有帳號？ <RouterLink to="/login">立即登入</RouterLink></p>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.kimi-login-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--kimi-bg-light);
  padding: 20px;
}

.kimi-login-container {
  display: flex;
  width: 100%;
  max-width: 900px;
  min-height: 560px;
  background: var(--kimi-card-light);
  border: 1px solid var(--kimi-border-light);
  overflow: hidden;
}

/* Left Brand Side */
.kimi-login-brand {
  flex: 1;
  padding: 48px 40px;
  background: var(--kimi-bg-light);
  display: flex;
  align-items: center;
  border-right: 1px solid var(--kimi-border-light);
}

.kimi-login-brand-content {
  width: 100%;
}

.kimi-login-logo {
  margin-bottom: 24px;
}

.kimi-login-logo .kimi-brand-mark {
  display: inline-grid;
  width: 48px;
  height: 48px;
  place-items: center;
  background: var(--kimi-text-light);
  color: var(--kimi-bg-light);
  font-size: 16px;
  font-weight: 700;
}

.kimi-login-title {
  font-size: 28px;
  font-weight: 700;
  letter-spacing: 0.1em;
  margin: 0 0 4px;
  color: var(--kimi-text-light);
}

.kimi-login-subtitle {
  font-size: 11px;
  letter-spacing: 0.2em;
  color: var(--kimi-muted);
  margin: 0 0 32px;
  text-transform: uppercase;
}

.kimi-login-features {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.kimi-feature-item {
  display: flex;
  align-items: flex-start;
  gap: 12px;
}

.kimi-feature-icon {
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--kimi-bg-light);
  border: 1px solid var(--kimi-border-light);
  font-size: 16px;
}

.kimi-feature-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--kimi-text-light);
}

.kimi-feature-desc {
  font-size: 12px;
  color: var(--kimi-muted);
  margin-top: 2px;
}

/* Right Form Side */
.kimi-login-form-wrapper {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 48px 40px;
}

.kimi-login-form {
  width: 100%;
  max-width: 320px;
}

.kimi-login-header {
  margin-bottom: 28px;
}

.kimi-login-header h2 {
  font-size: 22px;
  font-weight: 600;
  color: var(--kimi-text-light);
  margin: 0 0 4px;
}

.kimi-login-header p {
  font-size: 13px;
  color: var(--kimi-muted);
  margin: 0;
}

.kimi-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.kimi-form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.kimi-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--kimi-text-light);
  letter-spacing: 0.05em;
}

.kimi-input {
  width: 100%;
  height: 40px;
  padding: 0 14px;
  background: transparent;
  border: 1px solid var(--kimi-border-light);
  color: var(--kimi-text-light);
  font-size: 14px;
  outline: none;
  transition: border-color 0.2s;
}

.kimi-input::placeholder {
  color: var(--kimi-muted);
}

.kimi-input:focus {
  border-color: var(--kimi-text-light);
}

.kimi-error {
  padding: 10px 14px;
  background: rgba(248, 113, 113, 0.1);
  border: 1px solid rgba(248, 113, 113, 0.3);
  color: #f87171;
  font-size: 13px;
}

.kimi-btn {
  height: 44px;
  padding: 0 24px;
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.05em;
  cursor: pointer;
  transition: all 0.2s;
  border: 1px solid transparent;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}

.kimi-btn-primary {
  background: var(--kimi-text-light);
  color: var(--kimi-bg-light);
  border-color: var(--kimi-text-light);
}

.kimi-btn-primary:hover:not(:disabled) {
  background: #333;
  border-color: #333;
}

.kimi-btn-primary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.kimi-btn-full {
  width: 100%;
}

.kimi-spinner {
  width: 16px;
  height: 16px;
  border: 2px solid rgba(255, 255, 255, 0.3);
  border-top-color: #fff;
  border-radius: 50%;
  animation: spin 0.6s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

.kimi-login-footer {
  margin-top: 24px;
  text-align: center;
}

.kimi-login-footer p {
  font-size: 13px;
  color: var(--kimi-muted);
  margin: 0;
}

.kimi-login-footer a {
  color: var(--kimi-text-light);
  text-decoration: underline;
  text-underline-offset: 2px;
}

.kimi-login-footer a:hover {
  color: var(--kimi-text-light);
}

/* Responsive */
@media (max-width: 768px) {
  .kimi-login-container {
    flex-direction: column;
    max-width: 400px;
  }

  .kimi-login-brand {
    border-right: none;
    border-bottom: 1px solid var(--kimi-border-light);
    padding: 32px 24px;
  }

  .kimi-login-features {
    display: none;
  }

  .kimi-login-form-wrapper {
    padding: 32px 24px;
  }
}
</style>
