<template>
  <canvas ref="canvasRef" class="absolute inset-0 w-full h-full pointer-events-none" style="z-index: 0" />
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'

const props = defineProps<{
  theme?: 'warm' | 'red'
}>()

const canvasRef = ref<HTMLCanvasElement>()
let animId = 0

onMounted(() => {
  const canvas = canvasRef.value
  if (!canvas) return
  const ctx = canvas.getContext('2d')
  if (!ctx) return

  const isWarm = props.theme === 'warm'
  const glowColor = isWarm ? 'rgba(255, 107, 0, 0.08)' : 'rgba(139, 26, 43, 0.10)'
  const textColor = isWarm ? 'rgba(0, 0, 0,' : 'rgba(255, 255, 255,'

  const resize = () => {
    canvas.width = canvas.offsetWidth * window.devicePixelRatio
    canvas.height = canvas.offsetHeight * window.devicePixelRatio
    ctx.scale(window.devicePixelRatio, window.devicePixelRatio)
  }
  resize()
  window.addEventListener('resize', resize)

  const chars = ['2', '3', '5', '8', '.', '%', '+', '-', '$', 'N', 'T']
  const w = canvas.offsetWidth
  const h = canvas.offsetHeight

  const particles: { x: number; y: number; tx: number; ty: number; vx: number; vy: number; char: string; size: number; op: number; settled: boolean }[] = []
  for (let i = 0; i < 45; i++) {
    const a = Math.random() * Math.PI * 2
    const s = 1 + Math.random() * 3
    particles.push({
      x: Math.random() * w, y: Math.random() * h,
      tx: w * 0.2 + Math.random() * w * 0.6, ty: h * 0.3 + Math.random() * h * 0.4,
      vx: Math.cos(a) * s, vy: Math.sin(a) * s,
      char: chars[Math.floor(Math.random() * chars.length)],
      size: 10 + Math.random() * 14, op: 0.3 + Math.random() * 0.5, settled: false,
    })
  }

  const start = Date.now()
  const duration = 4000

  const trailCvs = document.createElement('canvas')
  trailCvs.width = w
  trailCvs.height = h
  const trailCtx = trailCvs.getContext('2d')

  const animate = () => {
    const elapsed = Date.now() - start
    const progress = Math.min(elapsed / duration, 1)
    const ease = 1 - Math.pow(1 - progress, 3)
    ctx.clearRect(0, 0, w, h)

    if (trailCtx) {
      trailCtx.fillStyle = isWarm ? '#F5F5F5' : '#0A0A0A'
      trailCtx.fillRect(0, 0, trailCvs.width, trailCvs.height)
    }

    particles.forEach(p => {
      if (!p.settled) {
        const dx = p.tx - p.x, dy = p.ty - p.y
        p.vx = dx * 0.02 * (1 - ease * 0.5) + p.vx * 0.95
        p.vy = dy * 0.02 * (1 - ease * 0.5) + p.vy * 0.95
        p.x += p.vx; p.y += p.vy
        if (Math.abs(dx) < 2 && Math.abs(dy) < 2 && progress > 0.9) p.settled = true
      } else {
        p.x += Math.sin(elapsed * 0.001 + p.tx) * 0.15
        p.y += Math.cos(elapsed * 0.001 + p.ty) * 0.1
      }
      if (trailCtx && !p.settled) {
        trailCtx.beginPath(); trailCtx.arc(p.x, p.y, p.size * 0.8, 0, Math.PI * 2)
        trailCtx.fillStyle = glowColor; trailCtx.fill()
      }
      ctx.save(); ctx.font = `${p.size}px 'Courier New', monospace`
      ctx.fillStyle = `${textColor} ${p.op * (0.3 + ease * 0.7)})`
      ctx.fillText(p.char, p.x, p.y); ctx.restore()
    })

    if (trailCtx) {
      ctx.save(); ctx.globalAlpha = 0.3; ctx.drawImage(trailCvs, 0, 0); ctx.restore()
    }
    animId = requestAnimationFrame(animate)
  }
  animate()

  onUnmounted(() => {
    cancelAnimationFrame(animId)
    window.removeEventListener('resize', resize)
  })
})
</script>
