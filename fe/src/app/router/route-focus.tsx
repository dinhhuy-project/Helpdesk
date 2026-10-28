import { useEffect, useRef } from 'react'
import { useLocation } from 'react-router-dom'

export function RouteFocus() {
  const location = useLocation()
  const previous = useRef(location.key)
  useEffect(() => {
    if (previous.current === location.key) return
    previous.current = location.key
    const frame = requestAnimationFrame(() => document.getElementById('main-content')?.focus())
    return () => cancelAnimationFrame(frame)
  }, [location.key])
  return null
}
