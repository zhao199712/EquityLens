import { useRef, useEffect } from 'react'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'

gsap.registerPlugin(ScrollTrigger)

interface BarChartProps {
  data: { label: string; value: number; color?: string }[]
  width?: number
  height?: number
  barHeight?: number
  textColor?: string
  darkMode?: boolean
}

export default function BarChart({
  data,
  width = 400,
  height,
  barHeight = 24,
  textColor = '#000000',
  darkMode = false,
}: BarChartProps) {
  const svgRef = useRef<SVGSVGElement>(null)
  const barsRef = useRef<(SVGRectElement | null)[]>([])

  const maxVal = Math.max(...data.map((d) => Math.abs(d.value)))
  const rowHeight = barHeight + 16
  const chartH = height || data.length * rowHeight + 20
  const labelWidth = 100
  const barMaxWidth = width - labelWidth - 80

  useEffect(() => {
    if (!svgRef.current) return

    barsRef.current.forEach((bar, i) => {
      if (!bar) return
      const w = bar.getAttribute('data-width') || '0'
      gsap.set(bar, { attr: { width: 0 } })
      gsap.to(bar, {
        attr: { width: Number(w) },
        duration: 0.8,
        delay: i * 0.1,
        ease: 'power2.out',
        scrollTrigger: {
          trigger: svgRef.current,
          start: 'top 85%',
        },
      })
    })
  }, [data])

  return (
    <svg ref={svgRef} width={width} height={chartH} viewBox={`0 0 ${width} ${chartH}`}>
      {data.map((item, i) => {
        const y = 10 + i * rowHeight
        const barW = (Math.abs(item.value) / maxVal) * barMaxWidth
        const barColor = item.color || (darkMode ? '#FFFFFF' : '#000000')

        return (
          <g key={`bar-${i}`}>
            {/* Label */}
            <text
              x={0}
              y={y + barHeight / 2 + 4}
              fill={darkMode ? '#FFFFFF' : '#000000'}
              fontSize={12}
              fontFamily="Inter, sans-serif"
            >
              {item.label}
            </text>

            {/* Bar */}
            <rect
              ref={(el) => { barsRef.current[i] = el }}
              x={labelWidth}
              y={y}
              width={barW}
              height={barHeight}
              fill={barColor}
              data-width={barW}
            />

            {/* Value */}
            <text
              x={labelWidth + barW + 8}
              y={y + barHeight / 2 + 4}
              fill={textColor}
              fontSize={12}
              fontFamily="Inter, sans-serif"
            >
              {item.value > 0 ? '+' : ''}
              {item.value}%
            </text>
          </g>
        )
      })}
    </svg>
  )
}
