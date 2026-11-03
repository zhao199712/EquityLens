import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  server: {
    host: '0.0.0.0',
  },
  build: {
    chunkSizeWarningLimit: 700,
    rolldownOptions: {
      output: {
        // 群組會連同依賴一起捕獲：vue-echarts 依賴 vue，若 echarts 群組先捕獲，Vue runtime 會被打進
        // echarts chunk，入口就得 modulepreload 整包 ECharts。用 priority 讓 vue-vendor 優先。
        codeSplitting: {
          groups: [
            { name: 'vue-vendor', test: /node_modules[\\/](@vue|vue|vue-router|pinia|vue-i18n|@intlify)[\\/]/, priority: 30 },
            { name: 'echarts', test: /node_modules[\\/](echarts|zrender|vue-echarts)[\\/]/, priority: 20 },
            { name: 'gsap', test: /node_modules[\\/]gsap[\\/]/, priority: 10 },
          ],
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    include: ['src/**/*.{test,spec}.ts'],
  },
})
