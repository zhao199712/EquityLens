import { useRef, useEffect } from 'react'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'

gsap.registerPlugin(ScrollTrigger)

interface RadarChartProps {
  dimensions: { label: string; value: number; max: number }[]
  width?: number
  height?: number
  fillColor?: string
  strokeColor?: string
}

export default function RadarChart({
  dimensions,
  width = 300,
  height = 300,
  fillColor = '#8B1A2B',
  strokeColor = '#8B1A2B',
}: RadarChartProps) {
  const svgRef = useRef<SVGSVGElement>(null)
  const polyRef = useRef<SVGPolygonElement>(null)

  const cx = width / 2
  const cy = height / 2
  const maxRadius = 110
  const levels = 5
  const angleStep = (Math.PI * 2) / dimensions.length
  const startAngle = -Math.PI / 2

  // Grid polygon points for each level
  const gridPolygons = Array.from({ length: levels }).map((_, level) => {
    const r = ((level + 1) / levels) * maxRadius
    return dimensions.map((_, i) => {
      const angle = startAngle + i * angleStep
      return [cx + r * Math.cos(angle), cy + r * Math.sin(angle)]
    })
  })

  // Data polygon points
  const dataPoints = dimensions.map((d, i) => {
    const angle = startAngle + i * angleStep
    const r = (d.value / d.max) * maxRadius
    return [cx + r * Math.cos(angle), cy + r * Math.sin(angle)]
  })

  const dataPolygonStr = dataPoints.map((p) => p.join(',')).join(' ')

  useEffect(() => {
    if (!polyRef.current) return
    gsap.set(polyRef.current, { opacity: 0, scale: 0.5, transformOrigin: `${cx}px ${cy}px` })
    gsap.to(polyRef.current, {
      opacity: 1,
      scale: 1,
      duration: 1,
      ease: 'power2.out',
      scrollTrigger: { trigger: svgRef.current, start: 'top 85%' },
    })
  }, [cx, cy])

  return (
    <svg ref={svgRef} width={width} height={height} viewBox={`0 0 ${width} ${height}`} className="w-full">
      {/* Grid polygons */}
      {gridPolygons.map((points, level) => (
        <polygon
          key={`grid-${level}`}
          points={points.map((p) => p.join(',')).join(' ')}
          fill="none"
          stroke="#333333"
          strokeWidth={1}
          strokeDasharray="4 4"
        />
      ))}

      {/* Axis lines */}
      {dimensions.map((_, i) => {
        const angle = startAngle + i * angleStep
        const x = cx + maxRadius * Math.cos(angle)
        const y = cy + maxRadius * Math.sin(angle)
        return (
          <line
            key={`axis-${i}`}
            x1={cx}
            y1={cy}
            x2={x}
            y2={y}
            stroke="#333333"
            strokeWidth={1}
          />
        )
      })}

      {/* Data polygon */}
      <polygon
        ref={polyRef}
        points={dataPolygonStr}
        fill={fillColor}
        fillOpacity={0.2}
        stroke={strokeColor}
        strokeWidth={2}
      />

      {/* Data points */}
      {dataPoints.map((p, i) => (
        <circle key={`dp-${i}`} cx={p[0]} cy={p[1]} r={5} fill={fillColor} />
      ))}

      {/* Dimension labels */}
      {dimensions.map((d, i) => {
        const angle = startAngle + i * angleStep
        const labelR = maxRadius + 22
        const x = cx + labelR * Math.cos(angle)
        const y = cy + labelR * Math.sin(angle)
        return (
          <text
            key={`label-${i}`}
            x={x}
            y={y + 4}
            textAnchor="middle"
            fill="#666666"
            fontSize={11}
            fontFamily="Inter, sans-serif"
          >
            {d.label}
          </text>
        )
      })}
    </svg>
  )
}
