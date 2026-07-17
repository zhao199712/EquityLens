<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'

interface Particle {
  x: number
  y: number
  vx: number
  vy: number
  radius: number
}

const props = withDefaults(defineProps<{
  density?: number
  linkDistance?: number
}>(), {
  density: 70,
  linkDistance: 120,
})

const canvasRef = ref<HTMLCanvasElement>()
let ctx: CanvasRenderingContext2D | null = null
let particles: Particle[] = []
let rafId = 0
let resizeObserver: ResizeObserver | null = null
const mouse = { x: -9999, y: -9999 }

function resize() {
  const canvas = canvasRef.value
  if (!canvas || !ctx) return
  const parent = canvas.parentElement
  if (!parent) return
  const dpr = window.devicePixelRatio || 1
  canvas.width = parent.clientWidth * dpr
  canvas.height = parent.clientHeight * dpr
  canvas.style.width = `${parent.clientWidth}px`
  canvas.style.height = `${parent.clientHeight}px`
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  seedParticles(parent.clientWidth, parent.clientHeight)
}

function seedParticles(width: number, height: number) {
  const count = Math.min(props.density, Math.floor((width * height) / 14000))
  particles = Array.from({ length: count }, () => ({
    x: Math.random() * width,
    y: Math.random() * height,
    vx: (Math.random() - 0.5) * 0.35,
    vy: (Math.random() - 0.5) * 0.35,
    radius: Math.random() * 1.6 + 0.6,
  }))
}

function tick() {
  const canvas = canvasRef.value
  if (!canvas || !ctx) return
  const width = canvas.clientWidth
  const height = canvas.clientHeight
  ctx.clearRect(0, 0, width, height)

  for (const p of particles) {
    // 滑鼠輕微排斥
    const dxm = p.x - mouse.x
    const dym = p.y - mouse.y
    const distM = Math.hypot(dxm, dym)
    if (distM < 100 && distM > 0.01) {
      p.vx += (dxm / distM) * 0.015
      p.vy += (dym / distM) * 0.015
    }
    p.x += p.vx
    p.y += p.vy
    // 限速避免滑鼠互動後飛走
    p.vx = Math.max(-0.6, Math.min(0.6, p.vx))
    p.vy = Math.max(-0.6, Math.min(0.6, p.vy))
    if (p.x < 0 || p.x > width) p.vx *= -1
    if (p.y < 0 || p.y > height) p.vy *= -1

    ctx.beginPath()
    ctx.arc(p.x, p.y, p.radius, 0, Math.PI * 2)
    ctx.fillStyle = 'rgba(34, 211, 238, 0.55)'
    ctx.fill()
  }

  // 鄰近連線
  for (let i = 0; i < particles.length; i++) {
    for (let j = i + 1; j < particles.length; j++) {
      const a = particles[i]
      const b = particles[j]
      const dist = Math.hypot(a.x - b.x, a.y - b.y)
      if (dist < props.linkDistance) {
        const alpha = (1 - dist / props.linkDistance) * 0.22
        ctx.beginPath()
        ctx.moveTo(a.x, a.y)
        ctx.lineTo(b.x, b.y)
        ctx.strokeStyle = `rgba(56, 189, 248, ${alpha})`
        ctx.lineWidth = 1
        ctx.stroke()
      }
    }
  }

  rafId = requestAnimationFrame(tick)
}

function handleMouseMove(event: MouseEvent) {
  const canvas = canvasRef.value
  if (!canvas) return
  const rect = canvas.getBoundingClientRect()
  mouse.x = event.clientX - rect.left
  mouse.y = event.clientY - rect.top
}

function handleMouseLeave() {
  mouse.x = -9999
  mouse.y = -9999
}

onMounted(() => {
  const canvas = canvasRef.value
  if (!canvas) return
  ctx = canvas.getContext('2d')
  resizeObserver = new ResizeObserver(resize)
  if (canvas.parentElement) resizeObserver.observe(canvas.parentElement)
  resize()
  canvas.parentElement?.addEventListener('mousemove', handleMouseMove)
  canvas.parentElement?.addEventListener('mouseleave', handleMouseLeave)
  rafId = requestAnimationFrame(tick)
})

onUnmounted(() => {
  cancelAnimationFrame(rafId)
  resizeObserver?.disconnect()
  const canvas = canvasRef.value
  canvas?.parentElement?.removeEventListener('mousemove', handleMouseMove)
  canvas?.parentElement?.removeEventListener('mouseleave', handleMouseLeave)
})
</script>

<template>
  <canvas ref="canvasRef" class="tech-particle-field" aria-hidden="true" />
</template>

<style scoped>
.tech-particle-field {
  position: absolute;
  inset: 0;
  pointer-events: none;
}
</style>
