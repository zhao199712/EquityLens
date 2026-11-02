interface FooterProps {
  isDark?: boolean
}

export default function Footer({ isDark = false }: FooterProps) {
  const bgColor = isDark ? '#0A0A0A' : '#F5F5F5'
  const borderColor = isDark ? '#333333' : '#E0E0E0'
  const label = isDark ? 'FINANCIALS' : 'PORTFOLIO'

  return (
    <footer
      className="w-full flex items-center justify-between px-5"
      style={{
        backgroundColor: bgColor,
        borderTop: `1px solid ${borderColor}`,
        height: '60px',
      }}
    >
      <span className="text-xs" style={{ color: '#666666' }}>
        RISE VISION 2026
      </span>
      <span
        className="text-xs font-mono tracking-wider uppercase"
        style={{ color: '#666666' }}
      >
        {label}
      </span>
      <span className="text-xs" style={{ color: '#666666' }}>
        數據僅供參考
      </span>
    </footer>
  )
}
