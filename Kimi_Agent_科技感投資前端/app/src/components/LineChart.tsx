import { useRef, useEffect } from 'react'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'

gsap.registerPlugin(ScrollTrigger)

interface LineChartProps {
  data: number[]
  labels: string[]
  yAxisLabels: string[]
  width?: number
  height?: number
  lineColor?: string
  fillColor?: string
  gridColor?: string
  textColor?: string
  darkMode?: boolean
  showArea?: boolean
  showPoints?: boolean
  secondLine?: number[]
  secondLineColor?: string
}

export default function LineChart({
  data,
  labels,
  yAxisLabels,
  width = 800,
  height = 400,
  lineColor = '#000000',
  fillColor,
  gridColor = '#E0E0E0',
  textColor = '#666666',
  darkMode = false,
  showArea = true,
  showPoints = true,
  secondLine,
  secondLineColor = '#8B1A2B',
}: LineChartProps) {
  const svgRef = useRef<SVGSVGElement>(null)
  const pathRef = useRef<SVGPathElement>(null)
  const areaRef = useRef<SVGPathElement>(null)
  const pointsRef = useRef<(SVGCircleElement | null)[]>([])

  const padding = { top: 20, right: 20, bottom: 40, left: 70 }
  const chartW = width - padding.left - padding.right
  const chartH = height - padding.top - padding.bottom

  const maxVal = Math.max(...data, ...(secondLine || []))
  const minVal = Math.min(...data, ...(secondLine || []))
  const range = maxVal - minVal || 1

  const getX = (i: number) => padding.left + (i / (data.length - 1)) * chartW
  const getY = (v: number) => padding.top + chartH - ((v - minVal) / range) * chartH

  const linePath = data
    .map((v, i) => `${i === 0 ? 'M' : 'L'} ${getX(i)} ${getY(v)}`)
    .join(' ')

  const areaPath = showArea
    ? `${linePath} L ${getX(data.length - 1)} ${padding.top + chartH} L ${getX(0)} ${padding.top + chartH} Z`
    : ''

  const secondLinePath = secondLine
    ? secondLine.map((v, i) => `${i === 0 ? 'M' : 'L'} ${getX(i)} ${getY(v)}`).join(' ')
    : ''

  useEffect(() => {
    if (!svgRef.current) return

    const tl = gsap.timeline({
      scrollTrigger: {
        trigger: svgRef.current,
        start: 'top 80%',
        toggleActions: 'play none none none',
      },
    })

    if (pathRef.current) {
      const length = pathRef.current.getTotalLength()
      gsap.set(pathRef.current, { strokeDasharray: length, strokeDashoffset: length })
      tl.to(pathRef.current, { strokeDashoffset: 0, duration: 2, ease: 'power2.out' })
    }

    if (secondLine && pathRef.current) {
      const secondPath = svgRef.current.querySelector('.second-line') as SVGPathElement
      if (secondPath) {
        const length = secondPath.getTotalLength()
        gsap.set(secondPath, { strokeDasharray: length, strokeDashoffset: length })
        tl.to(secondPath, { strokeDashoffset: 0, duration: 1.5, ease: 'power2.out' }, '-=1')
      }
    }

    if (areaRef.current) {
      gsap.set(areaRef.current, { opacity: 0 })
      tl.to(areaRef.current, { opacity: 1, duration: 1 }, '-=1')
    }

    pointsRef.current.forEach((point, i) => {
      if (point) {
        gsap.set(point, { scale: 0, transformOrigin: 'center' })
        tl.to(point, { scale: 1, duration: 0.3, ease: 'back.out(1.5)' }, `-=${1.8 - i * 0.05}`)
      }
    })

    return () => { tl.kill() }
  }, [data, secondLine])

  return (
    <svg ref={svgRef} width={width} height={height} className="w-full" viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="xMidYMid meet">
      {/* Grid lines */}
      {yAxisLabels.map((_, i) => {
        const y = padding.top + (i / (yAxisLabels.length - 1)) * chartH
        return (
          <line
            key={`grid-${i}`}
            x1={padding.left}
            y1={y}
            x2={padding.left + chartW}
            y2={y}
            stroke={gridColor}
            strokeWidth={1}
            strokeDasharray="4 4"
          />
        )
      })}

      {/* Y axis labels */}
      {yAxisLabels.map((label, i) => {
        const y = padding.top + chartH - (i / (yAxisLabels.length - 1)) * chartH
        return (
          <text
            key={`y-${i}`}
            x={padding.left - 10}
            y={y + 4}
            textAnchor="end"
            fill={textColor}
            fontSize={11}
            fontFamily="Inter, sans-serif"
          >
            {label}
          </text>
        )
      })}

      {/* X axis labels */}
      {labels.map((label, i) => (
        <text
          key={`x-${i}`}
          x={getX(i)}
          y={height - 10}
          textAnchor="middle"
          fill={textColor}
          fontSize={11}
          fontFamily="Inter, sans-serif"
        >
          {label}
        </text>
      ))}

      {/* Area fill */}
      {showArea && areaPath && (
        <path
          ref={areaRef}
          d={areaPath}
          fill={fillColor || (darkMode ? 'rgba(255,255,255,0.03)' : 'rgba(0,0,0,0.03)')}
        />
      )}

      {/* Main line */}
      <path
        ref={pathRef}
        d={linePath}
        fill="none"
        stroke={lineColor}
        strokeWidth={2}
      />

      {/* Second line */}
      {secondLine && secondLinePath && (
        <path
          className="second-line"
          d={secondLinePath}
          fill="none"
          stroke={secondLineColor}
          strokeWidth={2}
        />
      )}

      {/* Data points */}
      {showPoints &&
        data.map((v, i) => (
          <circle
            key={`pt-${i}`}
            ref={(el) => { pointsRef.current[i] = el }}
            cx={getX(i)}
            cy={getY(v)}
            r={4}
            fill={lineColor}
            className="cursor-pointer hover:r-6"
          />
        ))}
    </svg>
  )
}
