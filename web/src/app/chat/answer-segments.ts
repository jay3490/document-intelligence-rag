export type AnswerSegment = { kind: 'text'; text: string } | { kind: 'citation'; numbers: number[] };

const citationPattern = /\[(\d{1,4}(?:\s*,\s*\d{1,4})*)\]/g;

/**
 * Splits an answer into plain text and citation markers such as `[2]` or `[1, 3]`, so the
 * markers can be rendered as links to their sources. Mirrors the API's citation parser.
 */
export function splitAnswer(answer: string): AnswerSegment[] {
  const segments: AnswerSegment[] = [];
  let last = 0;

  for (const match of answer.matchAll(citationPattern)) {
    const index = match.index ?? 0;
    if (index > last) {
      segments.push({ kind: 'text', text: answer.slice(last, index) });
    }
    segments.push({ kind: 'citation', numbers: match[1].split(',').map((n) => Number(n.trim())) });
    last = index + match[0].length;
  }

  if (last < answer.length) {
    segments.push({ kind: 'text', text: answer.slice(last) });
  }
  return segments;
}
