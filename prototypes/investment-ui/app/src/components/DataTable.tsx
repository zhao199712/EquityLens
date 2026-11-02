import { useRef, useEffect } from 'react'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'

gsap.registerPlugin(ScrollTrigger)

interface DataTableProps {
  headers: string[]
  rows: (string | number | React.ReactNode)[][]
  darkMode?: boolean
  highlightRow?: number | null
  onRowHover?: (index: number | null) => void
  compact?: boolean
}

export default function DataTable({
  headers,
  rows,
  darkMode = false,
  highlightRow,
  onRowHover,
  compact = false,
}: DataTableProps) {
  const tableRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!tableRef.current) return
    const rowEls = tableRef.current.querySelectorAll('.data-row')
    rowEls.forEach((row, i) => {
      gsap.fromTo(
        row,
        { opacity: 0, x: -10 },
        {
          opacity: 1,
          x: 0,
          duration: 0.5,
          delay: i * 0.05,
          ease: 'power2.out',
          scrollTrigger: { trigger: tableRef.current, start: 'top 85%' },
        }
      )
    })
  }, [rows])

  const bgMain = darkMode ? '#0A0A0A' : '#FFFFFF'
  const bgAlt = darkMode ? '#111111' : '#F5F5F5'
  const borderCol = darkMode ? '#333333' : '#E0E0E0'
  const headerBg = darkMode ? '#333333' : '#000000'
  const headerText = '#FFFFFF'
  const textMain = darkMode ? '#FFFFFF' : '#000000'
  const textGray = '#666666'
  const rowH = compact ? 40 : 48

  return (
    <div ref={tableRef} className="w-full overflow-x-auto">
      <table className="w-full" style={{ borderCollapse: 'collapse' }}>
        <thead>
          <tr style={{ backgroundColor: headerBg }}>
            {headers.map((h, i) => (
              <th
                key={`h-${i}`}
                className="text-caption uppercase text-left px-4"
                style={{
                  color: headerText,
                  height: rowH,
                  borderBottom: `1px solid ${borderCol}`,
                  whiteSpace: 'nowrap',
                  fontSize: 11,
                  letterSpacing: '0.1em',
                }}
              >
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, rowIdx) => {
            const isHighlighted = highlightRow === rowIdx
            return (
              <tr
                key={`r-${rowIdx}`}
                className="data-row transition-colors duration-200"
                style={{
                  backgroundColor: isHighlighted
                    ? (darkMode ? '#1A1A1A' : '#000000')
                    : (rowIdx % 2 === 0 ? bgMain : bgAlt),
                  height: rowH,
                  cursor: onRowHover ? 'pointer' : 'default',
                }}
                onMouseEnter={() => onRowHover?.(rowIdx)}
                onMouseLeave={() => onRowHover?.(null)}
              >
                {row.map((cell, colIdx) => (
                  <td
                    key={`c-${rowIdx}-${colIdx}`}
                    className="px-4 text-sm whitespace-nowrap"
                    style={{
                      color: isHighlighted
                        ? '#FFFFFF'
                        : (colIdx === row.length - 1 && typeof cell === 'string' && cell.startsWith('+'))
                          ? textMain
                          : (colIdx === row.length - 1 && typeof cell === 'string' && cell.startsWith('-'))
                            ? textGray
                            : textMain,
                      fontWeight: colIdx === 0 ? 600 : 400,
                      borderBottom: `1px solid ${borderCol}`,
                    }}
                  >
                    {typeof cell === 'number' ? cell.toLocaleString() : cell}
                  </td>
                ))}
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
