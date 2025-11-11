/**
 * Simple Azure-friendly logger that uses console output
 * Logs are automatically captured by Azure Log Analytics
 */

class SimpleLogger {
  constructor() {
    this.serviceName = 'OmniAsylum-API';
  }

  formatMessage(level, category, message, data = null) {
    const timestamp = new Date().toISOString();
    const logEntry = {
      timestamp,
      level,
      category,
      service: this.serviceName,
      message,
      ...(data && { data })
    };

    return JSON.stringify(logEntry);
  }

  info(category, message, data) {
    console.log(this.formatMessage('INFO', category, message, data));
  }

  warn(category, message, data) {
    console.warn(this.formatMessage('WARN', category, message, data));
  }

  error(category, message, data) {
    console.error(this.formatMessage('ERROR', category, message, data));
  }

  debug(category, message, data) {
    if (process.env.NODE_ENV !== 'production') {
      console.log(this.formatMessage('DEBUG', category, message, data));
    }
  }

  success(category, message, data) {
    console.log(this.formatMessage('SUCCESS', category, message, data));
  }

  // Specialized logging methods
  apiRequest(method, url, userId, data) {
    this.info('API_REQUEST', `${method} ${url}`, {
      userId: userId || 'anonymous',
      ...data
    });
  }

  apiResponse(method, url, statusCode, duration, data) {
    const level = statusCode >= 500 ? 'ERROR' : statusCode >= 400 ? 'WARN' : 'INFO';
    this[level.toLowerCase()]('API_RESPONSE', `${method} ${url} - ${statusCode}`, {
      statusCode,
      duration: `${duration}ms`,
      ...data
    });
  }

  dbQuery(operation, table, query, result) {
    this.info('DATABASE', `${operation} on ${table}`, {
      operation,
      table,
      query: typeof query === 'string' ? query.substring(0, 100) : query,
      resultCount: Array.isArray(result) ? result.length : result ? 1 : 0
    });
  }

  authAttempt(userId, action, success, data) {
    const level = success ? 'INFO' : 'WARN';
    this[level]('AUTH', `${action} attempt`, {
      userId,
      action,
      success,
      ...data
    });
  }
}

// Create singleton instance
const logger = new SimpleLogger();

module.exports = logger;
