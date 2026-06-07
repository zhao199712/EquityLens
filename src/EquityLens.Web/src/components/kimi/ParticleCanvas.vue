<template>
  <canvas ref="canvasRef" class="kimi-particle-canvas" />
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'

interface Particle {
  x: number
  y: number
  targetX: number
  targetY: number
  vx: number
  vy: number
  char: string
  size: number
  opacity: number
  settled: boolean
}

const props = withDefaults(defineProps<{ theme?: 'warm' | 'dark' }>(), {
  theme: 'warm',
})

const canvasRef = ref<HTMLCanvasElement>()
let animationId = 0
let trailCanvas: HTMLCanvasElement | null = null

const chars = ['2', '3', '5', '8', '.', '%', '+', '-', '$', 'N', 'T']

onMounted(() => {
  const canvas = canvasRef.value
  if (!canvas) return
  const ctx = canvas.getContext('2d')
  if (!ctx) return

  trailCanvas = document.createElement('canvas')

  const glowColor = props.theme === 'warm' ? 'rgba(255, 107, 0, 0.08)' : 'rgba(139, 26, 43, 0.10)'

  const resize = () => {
    const dpr = window.devicePixelRatio || 1
    canvas.width = canvas.offsetWidth * dpr
    canvas.height = canvas.offsetHeight * dpr
    ctx.scale(dpr, dpr)
    if (trailCanvas) {
      trailCanvas.width = canvas.offsetWidth
      trailCanvas.height = canvas.offsetHeight
    }
  }
  resize()
  window.addEventListener('resize', resize)

  const w = canvas.offsetWidth
  const h = canvas.offsetHeight
  const particleCount = 45

  const particles: Particle[] = []
  for (let i = 0; i < particleCount; i++) {
    const angle = Math.random() * Math.PI * 2
    const speed = 1 + Math.random() * 3
    particles.push({
      x: Math.random() * w,
      y: Math.random() * h,
      targetX: w * 0.2 + Math.random() * w * 0.6,
      targetY: h * 0.3 + Math.random() * h * 0.4,
      vx: Math.cos(angle) * speed,
      vy: Math.sin(angle) * speed,
      char: chars[Math.floor(Math.random() * chars.length)],
      size: 10 + Math.random() * 14,
      opacity: 0.3 + Math.random() * 0.5,
      settled: false,
    })
  }

  const startTime = Date.now()
  const convergeDuration = 4000

  const animate = () => {
    const elapsed = Date.now() - startTime
    const progress = Math.min(elapsed / convergeDuration, 1)
    const easeOut = 1 - Math.pow(1 - progress, 3)

    ctx.clearRect(0, 0, canvas.offsetWidth, canvas.offsetHeight)

    const trailCtx = trailCanvas?.getContext('2d')
    if (trailCtx) {
      trailCtx.fillStyle = props.theme === 'warm' ? '#F5F5F5' : '#0A0A0A'
      trailCtx.fillRect(0, 0, trailCanvas!.width, trailCanvas!.height)
    }

    particles.forEach((p) => {
      if (!p.settled) {
        const dx = p.targetX - p.x
        const dy = p.targetY - p.y
        p.vx = dx * 0.02 * (1 - easeOut * 0.5) + p.vx * 0.95
        p.vy = dy * 0.02 * (1 - easeOut * 0.5) + p.vy * 0.95
        p.x += p.vx
        p.y += p.vy
        if (Math.abs(dx) < 2 && Math.abs(dy) < 2 && progress > 0.9) {
          p.settled = true
        }
      } else {
        p.x += Math.sin(elapsed * 0.001 + p.targetX) * 0.15
        p.y += Math.cos(elapsed * 0.001 + p.targetY) * 0.1
      }

      if (trailCtx && !p.settled) {
        trailCtx.beginPath()
        trailCtx.arc(p.x, p.y, p.size * 0.8, 0, Math.PI * 2)
        trailCtx.fillStyle = glowColor
        trailCtx.fill()
      }

      ctx.save()
      ctx.font = `${p.size}px 'Courier New', monospace`
      ctx.fillStyle = props.theme === 'warm'
        ? `rgba(0, 0, 0, ${p.opacity * (0.3 + easeOut * 0.7)})`
        : `rgba(255, 255, 255, ${p.opacity * (0.3 + easeOut * 0.7)})`
      ctx.fillText(p.char, p.x, p.y)
      ctx.restore()
    })

    if (trailCtx && trailCanvas) {
      ctx.save()
      ctx.globalAlpha = 0.3
      ctx.drawImage(trailCanvas, 0, 0)
      ctx.restore()
    }

    animationId = requestAnimationFrame(animate)
  }

  animate()
})

onUnmounted(() => {
  cancelAnimationFrame(animationId)
  window.removeEventListener('resize', () => {})
})
</script>

<style scoped>
.kimi-particle-canvas {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  pointer-events: none;
  z-index: 0;
}
</style>
