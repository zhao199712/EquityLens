<script setup lang="ts">
import { useRouter } from 'vue-router'

const router = useRouter()

interface StyleEntry {
  route: string
  name: string
  en: string
  desc: string
  swatches: string[]
  current?: boolean
}

const styles: StyleEntry[] = [
  {
    route: 'home-neon',
    name: '霓虹科技',
    en: 'NEON TECH',
    desc: '深藍黑底、青藍霓虹光效、HUD 網格與粒子,初版科技感設計。',
    swatches: ['#050a14', '#22d3ee', '#818cf8'],
  },
  {
    route: 'home-aurora',
    name: '極光玻璃',
    en: 'AURORA GLASS',
    desc: '深藍紫漸層與漂浮光球,大圓角毛玻璃卡片,柔和現代。',
    swatches: ['#1a1b4b', '#a78bfa', '#67e8f9'],
  },
  {
    route: 'home-swiss',
    name: '瑞士編輯',
    en: 'SWISS EDITORIAL',
    desc: '米白紙感、純黑配正紅,超大緊排標題與 hairline 格線的報紙式排版。',
    swatches: ['#f5f2ec', '#111111', '#e30613'],
  },
  {
    route: 'home-prestige',
    name: '私人銀行',
    en: 'PRESTIGE BANKING(目前首頁)',
    desc: '深夜藍配香檳金,細襯線標題與金框卡片,低調奢華的穩重感。',
    swatches: ['#0b1220', '#c9a86a', '#f5efe0'],
    current: true,
  },
  {
    route: 'home-analyst',
    name: '專業分析師',
    en: 'ANALYST PRO',
    desc: '淺灰藍底白卡藏青,數據密集的現代金融 SaaS 儀表板,克制無特效。',
    swatches: ['#eef2f7', '#1e3a5f', '#ffffff'],
  },
  {
    route: 'home-zen',
    name: '禪意軟極簡',
    en: 'ZEN SOFT MINIMAL',
    desc: '暖奶油底與大地色系,柔和長投影圓角卡,襯線標題與慢節奏留白。',
    swatches: ['#f7f3ec', '#b0654a', '#5c6650'],
  },
]
</script>

<template>
  <div class="lab-page">
    <header class="lab-head">
      <span class="lab-eyebrow">EQUITYLENS STYLE LAB</span>
      <h1 class="lab-title">首頁風格比較</h1>
      <p class="lab-sub">六種設計方向,同一份資料與內容結構。點擊卡片開啟完整頁面比較。</p>
    </header>

    <div class="lab-grid">
      <button
        v-for="s in styles"
        :key="s.route"
        class="lab-card"
        @click="router.push({ name: s.route })"
      >
        <div class="lab-swatches">
          <span v-for="(c, i) in s.swatches" :key="i" class="lab-swatch" :style="{ background: c }" />
        </div>
        <div class="lab-card-body">
          <span class="lab-card-en">{{ s.en }}</span>
          <span class="lab-card-name">
            {{ s.name }}
            <span v-if="s.current" class="lab-current">目前首頁</span>
          </span>
          <span class="lab-card-desc">{{ s.desc }}</span>
        </div>
        <span class="lab-arrow">→</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.lab-page {
  min-height: calc(100vh - 60px);
  background: #0b0e14;
  color: #e8ecf4;
  font-family: 'Inter', 'Noto Sans TC', sans-serif;
  padding: 72px 24px 96px;
}

.lab-head {
  width: min(1080px, 100%);
  margin: 0 auto 48px;
}

.lab-eyebrow {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.28em;
  color: #7d8aa8;
}

.lab-title {
  margin: 12px 0;
  font-size: clamp(32px, 5vw, 52px);
  font-weight: 800;
  letter-spacing: -0.02em;
}

.lab-sub {
  margin: 0;
  color: #94a0b8;
  font-size: 14px;
}

.lab-grid {
  width: min(1080px, 100%);
  margin: 0 auto;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 16px;
}

.lab-card {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 18px;
  padding: 24px;
  text-align: left;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 16px;
  cursor: pointer;
  font-family: inherit;
  color: inherit;
  transition: all 0.25s ease;
}

.lab-card:hover {
  background: rgba(255, 255, 255, 0.06);
  border-color: rgba(255, 255, 255, 0.2);
  transform: translateY(-3px);
}

.lab-swatches {
  display: flex;
  gap: 8px;
}

.lab-swatch {
  width: 36px;
  height: 36px;
  border-radius: 10px;
  border: 1px solid rgba(255, 255, 255, 0.12);
}

.lab-card-body {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.lab-card-en {
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.2em;
  color: #7d8aa8;
}

.lab-card-name {
  font-size: 19px;
  font-weight: 700;
  display: flex;
  align-items: center;
  gap: 10px;
}

.lab-current {
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.1em;
  padding: 3px 8px;
  border-radius: 999px;
  background: rgba(34, 211, 238, 0.12);
  color: #22d3ee;
  border: 1px solid rgba(34, 211, 238, 0.3);
}

.lab-card-desc {
  font-size: 13px;
  line-height: 1.7;
  color: #94a0b8;
}

.lab-arrow {
  position: absolute;
  top: 22px;
  right: 22px;
  color: #7d8aa8;
  transition: transform 0.25s ease, color 0.25s ease;
}

.lab-card:hover .lab-arrow {
  transform: translateX(4px);
  color: #e8ecf4;
}
</style>
