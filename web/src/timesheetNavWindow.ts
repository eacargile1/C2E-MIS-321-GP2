/** Local calendar; mirrors server policy (±1 month around this week) for navigation UX. */

function pad2(n: number) {
  return String(n).padStart(2, '0')
}

export function toYmd(d: Date) {
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`
}

export function startOfWeekMonday(d: Date) {
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate())
  const day = x.getDay()
  const diff = (day + 6) % 7
  x.setDate(x.getDate() - diff)
  return x
}

export function addCalendarMonths(d: Date, delta: number) {
  return new Date(d.getFullYear(), d.getMonth() + delta, d.getDate())
}

export function ymdDateCompare(a: Date, b: Date) {
  const ax = a.getFullYear() * 10000 + (a.getMonth() + 1) * 100 + a.getDate()
  const bx = b.getFullYear() * 10000 + (b.getMonth() + 1) * 100 + b.getDate()
  return ax - bx
}

export function timesheetNavMondayBounds() {
  const anchorMonday = startOfWeekMonday(new Date())
  return {
    minMonday: startOfWeekMonday(addCalendarMonths(anchorMonday, -1)),
    maxMonday: startOfWeekMonday(addCalendarMonths(anchorMonday, 1)),
  }
}

export function parseYmdLocal(ymd: string): Date | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(ymd)) return null
  const [y, m, d] = ymd.split('-').map(Number)
  const dt = new Date(y!, (m ?? 1) - 1, d ?? 1)
  return Number.isFinite(dt.getTime()) ? dt : null
}

/** If week Monday YYYY-MM-dd is outside ±1 month window, return nearest allowed Monday (still YYYY-MM-dd). */
export function clampTimesheetWeekMondayYmd(weekMondayYmd: string): { ymd: string; didClamp: boolean } {
  const parsed = parseYmdLocal(weekMondayYmd)
  if (!parsed || parsed.getDay() !== 1) return { ymd: weekMondayYmd, didClamp: false }
  const { minMonday, maxMonday } = timesheetNavMondayBounds()
  if (ymdDateCompare(parsed, minMonday) < 0) return { ymd: toYmd(minMonday), didClamp: true }
  if (ymdDateCompare(parsed, maxMonday) > 0) return { ymd: toYmd(maxMonday), didClamp: true }
  return { ymd: weekMondayYmd, didClamp: false }
}
