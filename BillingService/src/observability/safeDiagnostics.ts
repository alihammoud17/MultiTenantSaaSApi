const sensitivePatternReplacements: Array<[RegExp, string]> = [
  [/\b(authorization)\s*[:=]\s*bearer\s+[^\s,;]+/gi, '$1=Bearer [REDACTED]'],
  [/\bbearer\s+[A-Za-z0-9\-._~+/]+=*/gi, 'Bearer [REDACTED]'],
  [/\b(api[_-]?key|signing[_-]?secret|callback[_-]?secret|token|secret)\s*[:=]\s*[^,\s;]+/gi, '$1=[REDACTED]'],
  [/"(authorization|apiKey|signingSecret|callbackSecret|token|secret)"\s*:\s*"[^"]*"/gi, '"$1":"[REDACTED]"']
];

export function sanitizeDiagnosticText(value: string): string {
  let sanitized = value;
  for (const [pattern, replacement] of sensitivePatternReplacements) {
    sanitized = sanitized.replace(pattern, replacement);
  }

  return sanitized;
}
