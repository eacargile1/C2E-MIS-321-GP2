import type { OperationsAiInsight } from '../api'

export type AiReviewPanelProps = {
  title: string
  usedLlm: boolean
  llmNote: string | null
  insights: OperationsAiInsight[]
  questions: string[]
  /** e.g. "Reviewer checklist" vs "Questions / summary" */
  questionsHeading?: string
  noteSuggestions?: string[]
}

function severityLabel(s: string) {
  const x = s.toLowerCase()
  if (x === 'risk') return 'Risk'
  if (x === 'warn') return 'Warn'
  return 'Info'
}

export default function AiReviewPanel({
  title,
  usedLlm,
  llmNote,
  insights,
  questions,
  questionsHeading = 'Questions / summary',
  noteSuggestions,
}: AiReviewPanelProps) {
  return (
    <div className="card admin-card" style={{ marginTop: '0.75rem' }}>
      <h3 className="admin-h2" style={{ fontSize: '1rem' }}>
        {title}
      </h3>
      <p className="admin-hint" style={{ marginBottom: 8 }}>
        {usedLlm ? 'Used OpenAI for extra checks on top of rules.' : 'Rules-based checks only.'}
        {llmNote ? ` ${llmNote}` : null}
      </p>
      {insights.length === 0 ? (
        <p className="admin-hint">No flags for this review.</p>
      ) : (
        <ul className="admin-hint" style={{ paddingLeft: '1.25rem', marginTop: 0 }}>
          {insights.map((i) => (
            <li key={`${i.code}-${i.message}`} style={{ marginBottom: 6 }}>
              <strong>[{severityLabel(i.severity)}]</strong> {i.message}{' '}
              <span style={{ opacity: 0.75 }}>({i.source})</span>
            </li>
          ))}
        </ul>
      )}
      {questions.length > 0 ? (
        <>
          <p className="admin-hint" style={{ marginBottom: 4 }}>
            <strong>{questionsHeading}</strong>
          </p>
          <ul className="admin-hint" style={{ paddingLeft: '1.25rem', marginTop: 0 }}>
            {questions.map((q, idx) => (
              <li key={idx} style={{ marginBottom: 4 }}>
                {q}
              </li>
            ))}
          </ul>
        </>
      ) : null}
      {noteSuggestions && noteSuggestions.length > 0 ? (
        <>
          <p className="admin-hint" style={{ marginBottom: 4 }}>
            <strong>Optional note templates</strong> (paste/edit — not saved automatically)
          </p>
          <ul className="admin-hint" style={{ paddingLeft: '1.25rem', marginTop: 0 }}>
            {noteSuggestions.map((n, idx) => (
              <li key={idx} style={{ marginBottom: 6 }}>
                <code style={{ fontSize: '0.85em', wordBreak: 'break-word' }}>{n}</code>
              </li>
            ))}
          </ul>
        </>
      ) : null}
    </div>
  )
}
