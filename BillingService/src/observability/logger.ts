export interface LogContext {
  [key: string]: unknown;
}

function write(level: 'INFO' | 'WARN' | 'ERROR', event: string, context: LogContext = {}) {
  const payload = {
    timestamp: new Date().toISOString(),
    level,
    event,
    ...context
  };

  const line = JSON.stringify(payload);
  if (level === 'ERROR') {
    console.error(line);
    return;
  }

  if (level === 'WARN') {
    console.warn(line);
    return;
  }

  console.info(line);
}

export const logger = {
  info(event: string, context?: LogContext) {
    write('INFO', event, context);
  },
  warn(event: string, context?: LogContext) {
    write('WARN', event, context);
  },
  error(event: string, context?: LogContext) {
    write('ERROR', event, context);
  }
};
