import { useRef, useEffect } from 'react'

interface ParticleCanvasProps {
  theme?: 'warm' | 'red'
}

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

export default function ParticleCanvas({ theme = 'warm' }: ParticleCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const particlesRef = useRef<Particle[]>([])
  const animationRef = useRef<number>(0)
  const trailCanvasRef = useRef<HTMLCanvasElement | null>(null)

  const glowColor = theme === 'warm'
    ? 'rgba(255, 107, 0, 0.08)'
    : 'rgba(139, 26, 43, 0.10)'

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return

    const ctx = canvas.getContext('2d')
    if (!ctx) return

    // Create trail canvas
    const trailCanvas = document.createElement('canvas')
    trailCanvas.width = canvas.width
    trailCanvas.height = canvas.height
    trailCanvasRef.current = trailCanvas

    const resize = () => {
      canvas.width = canvas.offsetWidth * window.devicePixelRatio
      canvas.height = canvas.offsetHeight * window.devicePixelRatio
      ctx.scale(window.devicePixelRatio, window.devicePixelRatio)
      trailCanvas.width = canvas.offsetWidth
      trailCanvas.height = canvas.offsetHeight
    }
    resize()
    window.addEventListener('resize', resize)

    // Initialize particles
    const chars = ['2', '3', '5', '8', '.', '%', '+', '-', '$', 'N', 'T']
    const particleCount = 45
    const w = canvas.offsetWidth
    const h = canvas.offsetHeight

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
    particlesRef.current = particles

    const startTime = Date.now()
    const convergeDuration = 4000

    const animate = () => {
      const elapsed = Date.now() - startTime
      const progress = Math.min(elapsed / convergeDuration, 1)
      const easeOut = 1 - Math.pow(1 - progress, 3)

      ctx.clearRect(0, 0, canvas.offsetWidth, canvas.offsetHeight)

      // Trail effect
      const trailCtx = trailCanvas.getContext('2d')
      if (trailCtx) {
        trailCtx.fillStyle = theme === 'warm' ? '#F5F5F5' : '#0A0A0A'
        trailCtx.fillRect(0, 0, trailCanvas.width, trailCanvas.height)
      }

      particles.forEach((p) => {
        if (!p.settled) {
          // Move toward target with ease-out
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
          // Slow drift after settling
          p.x += Math.sin(elapsed * 0.001 + p.targetX) * 0.15
          p.y += Math.cos(elapsed * 0.001 + p.targetY) * 0.1
        }

        // Draw glow trail
        if (trailCtx && !p.settled) {
          trailCtx.beginPath()
          trailCtx.arc(p.x, p.y, p.size * 0.8, 0, Math.PI * 2)
          trailCtx.fillStyle = glowColor
          trailCtx.fill()
        }

        // Draw character
        ctx.save()
        ctx.font = `${p.size}px 'Courier New', monospace`
        ctx.fillStyle = theme === 'warm'
          ? `rgba(0, 0, 0, ${p.opacity * (0.3 + easeOut * 0.7)})`
          : `rgba(255, 255, 255, ${p.opacity * (0.3 + easeOut * 0.7)})`
        ctx.fillText(p.char, p.x, p.y)
        ctx.restore()
      })

      // Composite trail
      if (trailCtx) {
        ctx.save()
        ctx.globalAlpha = 0.3
        ctx.drawImage(trailCanvas, 0, 0)
        ctx.restore()
      }

      animationRef.current = requestAnimationFrame(animate)
    }

    animate()

    return () => {
      cancelAnimationFrame(animationRef.current)
      window.removeEventListener('resize', resize)
    }
  }, [theme, glowColor])

  return (
    <canvas
      ref={canvasRef}
      className="absolute inset-0 w-full h-full pointer-events-none"
      style={{ zIndex: 0 }}
    />
  )
}
