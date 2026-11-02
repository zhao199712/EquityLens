import { useState } from 'react'

interface DataPoint {
  x: number
  y: number
  label: string
}

interface ScatterPlotProps {
  data: DataPoint[]
  xAxisLabel: string
  yAxisLabel: string
  xRange: [number, number]
  yRange: [number, number]
  width?: number
  height?: number
  frontierCurve?: [number, number][]
}

export default function ScatterPlot({
  data,
  xAxisLabel,
  yAxisLabel,
  xRange,
  yRange,
  width = 300,
  height = 300,
  frontierCurve,
}: ScatterPlotProps) {
  const [hoveredPoint, setHoveredPoint] = useState<number | null>(null)

  const padding = { top: 20, right: 20, bottom: 50, left: 55 }
  const chartW = width - padding.left - padding.right
  const chartH = height - padding.top - padding.bottom

  const xMin = xRange[0]
  const xMax = xRange[1]
  const yMin = yRange[0]
  const yMax = yRange[1]

  const getX = (v: number) => padding.left + ((v - xMin) / (xMax - xMin)) * chartW
  const getY = (v: number) => padding.top + chartH - ((v - yMin) / (yMax - yMin)) * chartH

  const xTicks = 6
  const yTicks = 6

  return (
    <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} className="w-full">
      {/* Grid */}
      {Array.from({ length: xTicks }).map((_, i) => {
        const x = padding.left + (i / (xTicks - 1)) * chartW
        return (
          <line
            key={`xgrid-${i}`}
            x1={x}
            y1={padding.top}
            x2={x}
            y2={padding.top + chartH}
            stroke="#E0E0E0"
            strokeWidth={1}
            strokeDasharray="4 4"
          />
        )
      })}
      {Array.from({ length: yTicks }).map((_, i) => {
        const y = padding.top + (i / (yTicks - 1)) * chartH
        return (
          <line
            key={`ygrid-${i}`}
            x1={padding.left}
            y1={y}
            x2={padding.left + chartW}
            y2={y}
            stroke="#E0E0E0"
            strokeWidth={1}
            strokeDasharray="4 4"
          />
        )
      })}

      {/* Axes labels */}
      {Array.from({ length: xTicks }).map((_, i) => {
        const val = xMin + (i / (xTicks - 1)) * (xMax - xMin)
        const x = padding.left + (i / (xTicks - 1)) * chartW
        return (
          <text key={`xtick-${i}`} x={x} y={height - 15} textAnchor="middle" fill="#666666" fontSize={10}>
            {Math.round(val)}%
          </text>
        )
      })}
      {Array.from({ length: yTicks }).map((_, i) => {
        const val = yMin + (i / (yTicks - 1)) * (yMax - yMin)
        const y = padding.top + chartH - (i / (yTicks - 1)) * chartH
        return (
          <text key={`ytick-${i}`} x={padding.left - 8} y={y + 3} textAnchor="end" fill="#666666" fontSize={10}>
            {Math.round(val)}%
          </text>
        )
      })}

      {/* Axis names */}
      <text
        x={padding.left + chartW / 2}
        y={height - 2}
        textAnchor="middle"
        fill="#666666"
        fontSize={11}
      >
        {xAxisLabel}
      </text>
      <text
        x={12}
        y={padding.top + chartH / 2}
        textAnchor="middle"
        fill="#666666"
        fontSize={11}
        transform={`rotate(-90, 12, ${padding.top + chartH / 2})`}
      >
        {yAxisLabel}
      </text>

      {/* Frontier curve */}
      {frontierCurve && (
        <path
          d={frontierCurve
            .map(([x, y], i) => `${i === 0 ? 'M' : 'L'} ${getX(x)} ${getY(y)}`)
            .join(' ')}
          fill="none"
          stroke="#999999"
          strokeWidth={1}
          strokeDasharray="4 4"
        />
      )}

      {/* Data points */}
      {data.map((point, i) => (
        <g
          key={`pt-${i}`}
          onMouseEnter={() => setHoveredPoint(i)}
          onMouseLeave={() => setHoveredPoint(null)}
          className="cursor-pointer"
        >
          <circle
            cx={getX(point.x)}
            cy={getY(point.y)}
            r={hoveredPoint === i ? 10 : 6}
            fill="#000000"
            className="transition-all duration-200"
          />
          {hoveredPoint === i && (
            <g>
              <rect
                x={getX(point.x) + 12}
                y={getY(point.y) - 25}
                width={100}
                height={22}
                fill="#000000"
              />
              <text
                x={getX(point.x) + 17}
                y={getY(point.y) - 10}
                fill="#FFFFFF"
                fontSize={11}
              >
                {point.label}: {point.x}%, {point.y}%
              </text>
            </g>
          )}
        </g>
      ))}
    </svg>
  )
}
