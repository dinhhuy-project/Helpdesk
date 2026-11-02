import type { KnowledgeGuide } from '../api/community-api'

export const emptyKnowledgeGuide: KnowledgeGuide = { applicability: '', prerequisites: '', expectedOutcome: '', escalation: '', sourceName: '', sourceUrl: null, sourceUpdatedOn: null }
export function normalizedSteps(content: string) {
  return content.split(/\r?\n/).map(line => line.trim()).filter(Boolean).map((line, index) => `${index + 1}. ${line.replace(/^\d+[.)]\s*/, '')}`).join('\n')
}
export function guideComplete(guide: KnowledgeGuide | null, content: string) {
  if (!guide || ![guide.applicability, guide.prerequisites, guide.expectedOutcome, guide.escalation, guide.sourceName].every(value => value.trim())) return false
  const text = content.trim().replace(/\r\n/g, '\n')
  const markers = [...text.matchAll(/^([0-9]+)[.)]([ \t]*)/gm)]
  return markers.length > 0 && markers.length <= 20 && markers[0]?.index === 0 && markers.every((match, index) => {
    const start = (match.index ?? 0) + match[0].length
    const body = text.slice(start, markers[index + 1]?.index).trim()
    return match[1] === String(index + 1) && (match[2]?.length ?? 0) > 0 && body.length > 0 && body.length <= 4000
  })
}
