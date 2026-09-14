import { useNavigate, useLocation } from 'react-router-dom'
import { useRef, useEffect, useState, useCallback } from 'react'
import gsap from 'gsap'

interface NavigationHeaderProps {
  isDark?: boolean
}

export default function NavigationHeader({ isDark = false }: NavigationHeaderProps) {
  const navigate = useNavigate()
  const location = useLocation()
  const titleRef = useRef<HTMLDivElement>(null)
  const subtitleRef = useRef<HTMLParagraphElement>(null)
  const navRef = useRef<HTMLDivElement>(null)
  const [isTransitioning, setIsTransitioning] = useState(false)

  const isPortfolio = location.pathname === '/'
  const bgColor = isDark ? '#0A0A0A' : '#F5F5F5'
  const textColor = isDark ? '#FFFFFF' : '#000000'
  const borderColor = isDark ? '#333333' : '#E0E0E0'
  const subtitleText = isPortfolio
    ? '即時追蹤投資組合表現，智能分析收益與風險，數據驅動的資產配置洞察'
    : '深度解讀企業財務數據，透視營收結構與獲利能力，掌握財務健康趨勢'
  const mainTitle1 = isPortfolio ? 'PORTFOLIO' : 'FINANCIAL'
  const mainTitle2 = isPortfolio ? 'ANALYSIS' : 'STATEMENTS'

  useEffect(() => {
    const tl = gsap.timeline()

    if (titleRef.current) {
      gsap.set(titleRef.current, { filter: 'blur(8px)', opacity: 0 })
      tl.to(titleRef.current, {
        filter: 'blur(0px)',
        opacity: 1,
        duration: 1.2,
        ease: 'cubic-bezier(0.22, 1, 0.36, 1)',
      })
    }

    if (subtitleRef.current) {
      gsap.set(subtitleRef.current, { opacity: 0, y: 10 })
      tl.to(
        subtitleRef.current,
        {
          opacity: 1,
          y: 0,
          duration: 0.8,
          ease: 'cubic-bezier(0.22, 1, 0.36, 1)',
        },
        '-=0.4'
      )
    }

    return () => { tl.kill() }
  }, [location.pathname])

  const handleNavClick = useCallback(
    (path: string) => {
      if (isTransitioning || location.pathname === path) return
      setIsTransitioning(true)

      const tl = gsap.timeline()

      if (navRef.current) {
        tl.to(navRef.current, {
          y: '100%',
          opacity: 0,
          duration: 0.5,
          ease: 'power2.inOut',
        })
      }

      tl.call(() => {
        navigate(path)
        setIsTransitioning(false)
      })
    },
    [isTransitioning, location.pathname, navigate]
  )

  return (
    <div
      className="relative w-full flex flex-col items-center justify-center font-heading"
      style={{
        backgroundColor: bgColor,
        color: textColor,
        minHeight: '100vh',
        padding: 'clamp(20px, 4vw, 80px)',
      }}
    >
      <div ref={navRef} className="flex flex-col items-center gap-8 w-full max-w-4xl">
        {/* Brand */}
        <div className="flex flex-col items-center gap-1">
          <span
            className="font-mono text-sm tracking-[0.3em]"
            style={{ color: textColor }}
          >
            睿見 RISE VISION
          </span>
          <span
            className="text-xs tracking-[0.2em] uppercase"
            style={{ color: isDark ? '#666666' : '#666666' }}
          >
            INVESTMENT ANALYTICS
          </span>
        </div>

        {/* Navigation Options */}
        <div className="flex items-center gap-10">
          <button
            onClick={() => handleNavClick('/')}
            className="text-xs font-medium tracking-[0.15em] uppercase transition-all duration-400"
            style={{
              color: isPortfolio ? textColor : isDark ? '#666666' : '#666666',
              fontWeight: isPortfolio ? 700 : 500,
              cursor: 'pointer',
              background: 'none',
              border: 'none',
            }}
            onMouseEnter={(e) => {
              if (!isPortfolio) {
                gsap.to(e.currentTarget, { y: -4, duration: 0.3 })
              }
            }}
            onMouseLeave={(e) => {
              if (!isPortfolio) {
                gsap.to(e.currentTarget, { y: 0, duration: 0.3 })
              }
            }}
          >
            投資組合分析
          </button>
          <span style={{ color: borderColor }}>|</span>
          <button
            onClick={() => handleNavClick('/financials')}
            className="text-xs font-medium tracking-[0.15em] uppercase transition-all duration-400"
            style={{
              color: !isPortfolio ? textColor : isDark ? '#666666' : '#666666',
              fontWeight: !isPortfolio ? 700 : 500,
              cursor: 'pointer',
              background: 'none',
              border: 'none',
            }}
            onMouseEnter={(e) => {
              if (isPortfolio) {
                gsap.to(e.currentTarget, { y: -4, duration: 0.3 })
              }
            }}
            onMouseLeave={(e) => {
              if (isPortfolio) {
                gsap.to(e.currentTarget, { y: 0, duration: 0.3 })
              }
            }}
          >
            財報分析
          </button>
        </div>

        {/* Page Title */}
        <div ref={titleRef} className="flex flex-col items-center gap-0">
          <h1
            className="text-display font-semibold tracking-tight text-center leading-none"
            style={{ color: textColor }}
          >
            {mainTitle1}
          </h1>
          <h1
            className="text-display font-semibold tracking-tight text-center leading-none"
            style={{ color: textColor }}
          >
            {mainTitle2}
          </h1>
        </div>

        {/* Subtitle */}
        <p
          ref={subtitleRef}
          className="text-sm text-center max-w-md leading-relaxed"
          style={{ color: '#666666' }}
        >
          {subtitleText}
        </p>
      </div>
    </div>
  )
}
