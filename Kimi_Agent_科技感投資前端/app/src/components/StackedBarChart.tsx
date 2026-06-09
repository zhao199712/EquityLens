import { useRef, useEffect } from 'react'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'

gsap.registerPlugin(ScrollTrigger)

interface StackedBarChartProps {
  data: { label: string; values: number[] }[]
  colors: string[]
  labels: string[]
  yAxisLabels: string[]
  width?: number
  height?: number
  lineData?: number[]
  lineColor?: string
}

export default function StackedBarChart({
  data,
  colors,
  labels: legendLabels,
  yAxisLabels,
  width = 800,
  height = 380,
  lineData,
  lineColor = '#8B1A2B',
}: StackedBarChartProps) {
  const svgRef = useRef<SVGSVGElement>(null)

  const padding = { top: 20, right: 60, bottom: 50, left: 60 }
  const chartW = width - padding.left - padding.right
  const chartH = height - padding.top - padding.bottom

  const maxVal = Math.max(
    ...data.map((d) => d.values.reduce((s, v) => s + Math.max(0, v), 0)),
    ...(lineData || [0])
  )
  const minVal = Math.min(
    ...data.map((d) => d.values.reduce((s, v) => s + Math.min(0, v), 0)),
    ...(lineData || [0])
  )
  const range = maxVal - minVal || 1

  const zeroY = padding.top + chartH - ((0 - minVal) / range) * chartH

  const barGroupWidth = chartW / data.length
  const barWidth = Math.min(barGroupWidth * 0.5, 30)

  const getBarH = (v: number) => Math.abs((v / range) * chartH)
  const getLineY = (v: number) => padding.top + chartH - ((v - minVal) / range) * chartH

  useEffect(() => {
    if (!svgRef.current) return
    const bars = svgRef.current.querySelectorAll('.stack-bar')
    bars.forEach((bar, i) => {
      gsap.fromTo(
        bar,
        { scaleY: 0, transformOrigin: `${zeroY < padding.top + chartH / 2 ? 'bottom' : 'top'}` },
        {
          scaleY: 1,
          duration: 0.8,
          delay: i * 0.05,
          ease: 'power2.out',
          scrollTrigger: { trigger: svgRef.current, start: 'top 80%' },
        }
      )
    })

    if (lineData) {
      const line = svgRef.current.querySelector('.cf-line') as SVGPathElement
      if (line) {
        const length = line.getTotalLength()
        gsap.set(line, { strokeDasharray: length, strokeDashoffset: length })
        gsap.to(line, {
          strokeDashoffset: 0,
          duration: 1.5,
          ease: 'power2.out',
          scrollTrigger: { trigger: svgRef.current, start: 'top 80%' },
        })
      }
    }
  }, [data, lineData, zeroY])

  return (
    <svg ref={svgRef} width={width} height={height} viewBox={`0 0 ${width} ${height}`} className="w-full">
      {/* Grid lines */}
      {yAxisLabels.map((_, i) => {
        const y = padding.top + (i / (yAxisLabels.length - 1)) * chartH
        return (
          <line
            key={`grid-${i}`}
            x1={padding.left}
            y1={y}
            x2={width - padding.right}
            y2={y}
            stroke="#333333"
            strokeWidth={1}
            strokeDasharray="4 4"
          />
        )
      })}

      {/* Zero line */}
      <line
        x1={padding.left}
        y1={zeroY}
        x2={width - padding.right}
        y2={zeroY}
        stroke="#666666"
        strokeWidth={2}
      />

      {/* Y axis labels */}
      {yAxisLabels.map((label, i) => {
        const y = padding.top + chartH - (i / (yAxisLabels.length - 1)) * chartH
        return (
          <text key={`y-${i}`} x={padding.left - 10} y={y + 4} textAnchor="end" fill="#666666" fontSize={11}>
            {label}
          </text>
        )
      })}

      {/* Bars */}
      {data.map((group, gi) => {
        const x = padding.left + gi * barGroupWidth + barGroupWidth / 2 - barWidth / 2
        let currentY = zeroY

        return (
          <g key={`group-${gi}`}>
            {group.values.map((v, vi) => {
              const barH = getBarH(v)
              const y = v >= 0 ? currentY - barH : currentY
              if (v >= 0) currentY -= barH
              else currentY += barH

              return (
                <rect
                  key={`bar-${gi}-${vi}`}
                  className="stack-bar"
                  x={x}
                  y={y}
                  width={barWidth}
                  height={barH}
                  fill={colors[vi]}
                />
              )
            })}
            {/* X label */}
            <text
              x={x + barWidth / 2}
              y={height - 15}
              textAnchor="middle"
              fill="#666666"
              fontSize={11}
            >
              {group.label}
            </text>
          </g>
        )
      })}

      {/* Line overlay */}
      {lineData && (
        <g>
          <path
            className="cf-line"
            d={lineData
              .map((v, i) => {
                const x = padding.left + i * barGroupWidth + barGroupWidth / 2
                const y = getLineY(v)
                return `${i === 0 ? 'M' : 'L'} ${x} ${y}`
              })
              .join(' ')}
            fill="none"
            stroke={lineColor}
            strokeWidth={2}
          />
          {lineData.map((v, i) => {
            const x = padding.left + i * barGroupWidth + barGroupWidth / 2
            const y = getLineY(v)
            return <circle key={`lp-${i}`} cx={x} cy={y} r={4} fill={lineColor} />
          })}
        </g>
      )}

      {/* Legend */}
      <g transform={`translate(${padding.left}, ${height - 5})`}>
        {legendLabels.map((label, i) => (
          <g key={`legend-${i}`} transform={`translate(${i * 120}, 0)`}>
            <rect x={0} y={-8} width={10} height={10} fill={colors[i]} />
            <text x={16} y={0} fill="#666666" fontSize={10}>
              {label}
            </text>
          </g>
        ))}
      </g>
    </svg>
  )
}
