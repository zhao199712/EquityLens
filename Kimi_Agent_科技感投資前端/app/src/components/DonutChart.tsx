import { useRef, useEffect, useState } from 'react'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'

gsap.registerPlugin(ScrollTrigger)

interface DonutChartProps {
  segments: { label: string; value: number; color: string; borderColor?: string }[]
  centerLabel?: string
  centerSubLabel?: string
  width?: number
  height?: number
  activeIndex?: number | null
  onSegmentHover?: (index: number | null) => void
}

export default function DonutChart({
  segments,
  centerLabel,
  centerSubLabel,
  width = 360,
  height = 360,
  activeIndex,
  onSegmentHover,
}: DonutChartProps) {
  const svgRef = useRef<SVGSVGElement>(null)
  const segmentsRef = useRef<(SVGPathElement | null)[]>([])
  const [hovered, setHovered] = useState<number | null>(null)

  const cx = width / 2
  const cy = height / 2
  const outerR = 140
  const innerR = 90
  const total = segments.reduce((s, seg) => s + seg.value, 0)

  let startAngle = -Math.PI / 2

  const paths = segments.map((seg, i) => {
    const angle = (seg.value / total) * Math.PI * 2
    const endAngle = startAngle + angle

    const x1 = cx + outerR * Math.cos(startAngle)
    const y1 = cy + outerR * Math.sin(startAngle)
    const x2 = cx + outerR * Math.cos(endAngle)
    const y2 = cy + outerR * Math.sin(endAngle)
    const x3 = cx + innerR * Math.cos(endAngle)
    const y3 = cy + innerR * Math.sin(endAngle)
    const x4 = cx + innerR * Math.cos(startAngle)
    const y4 = cy + innerR * Math.sin(startAngle)

    const largeArc = angle > Math.PI ? 1 : 0

    const d = `M ${x1} ${y1} A ${outerR} ${outerR} 0 ${largeArc} 1 ${x2} ${y2} L ${x3} ${y3} A ${innerR} ${innerR} 0 ${largeArc} 0 ${x4} ${y4} Z`

    const midAngle = startAngle + angle / 2
    startAngle = endAngle

    return { d, midAngle, seg, i }
  })

  useEffect(() => {
    if (!svgRef.current) return

    segmentsRef.current.forEach((seg, i) => {
      if (!seg) return
      gsap.set(seg, { opacity: 0, scale: 0.8, transformOrigin: `${cx}px ${cy}px` })
      gsap.to(seg, {
        opacity: 1,
        scale: 1,
        duration: 0.6,
        delay: i * 0.1,
        ease: 'power2.out',
        scrollTrigger: { trigger: svgRef.current, start: 'top 85%' },
      })
    })
  }, [cx, cy])

  const handleHover = (index: number | null) => {
    setHovered(index)
    onSegmentHover?.(index)
  }

  const currentHover = hovered !== null ? hovered : activeIndex

  return (
    <svg ref={svgRef} width={width} height={height} viewBox={`0 0 ${width} ${height}`}>
      {paths.map(({ d, midAngle, seg, i }) => {
        const isActive = currentHover === i
        const expandX = isActive ? 5 * Math.cos(midAngle) : 0
        const expandY = isActive ? 5 * Math.sin(midAngle) : 0

        return (
          <g key={`seg-${i}`}>
            <path
              ref={(el) => { segmentsRef.current[i] = el }}
              d={d}
              fill={seg.color}
              stroke={seg.borderColor || 'none'}
              strokeWidth={seg.borderColor ? 1 : 0}
              transform={`translate(${expandX}, ${expandY})`}
              className="cursor-pointer transition-transform duration-300"
              onMouseEnter={() => handleHover(i)}
              onMouseLeave={() => handleHover(null)}
            />
            {/* Percentage line + label */}
            <line
              x1={cx + outerR * Math.cos(midAngle)}
              y1={cy + outerR * Math.sin(midAngle)}
              x2={cx + (outerR + 30) * Math.cos(midAngle)}
              y2={cy + (outerR + 30) * Math.sin(midAngle)}
              stroke="#E0E0E0"
              strokeWidth={1}
            />
            <text
              x={cx + (outerR + 40) * Math.cos(midAngle)}
              y={cy + (outerR + 40) * Math.sin(midAngle) + 4}
              textAnchor={Math.cos(midAngle) > 0 ? 'start' : 'end'}
              fill="#666666"
              fontSize={11}
              fontFamily="Inter, sans-serif"
            >
              {seg.label} {Math.round((seg.value / total) * 100)}%
            </text>
          </g>
        )
      })}

      {/* Center label */}
      {centerLabel && (
        <g>
          <text
            x={cx}
            y={cy - 6}
            textAnchor="middle"
            fill="#000000"
            fontSize={20}
            fontWeight={600}
            fontFamily="Noto Sans TC, sans-serif"
          >
            {centerLabel}
          </text>
          {centerSubLabel && (
            <text
              x={cx}
              y={cy + 14}
              textAnchor="middle"
              fill="#666666"
              fontSize={11}
              fontFamily="Inter, sans-serif"
            >
              {centerSubLabel}
            </text>
          )}
        </g>
      )}
    </svg>
  )
}
