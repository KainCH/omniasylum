/**
 * Simple Console Logger for Azure Container Apps
 *
 * This logger uses structured console output that gets automatically
 * captured by Azure Log Analytics workspace for querying via KQL.
 *
 * No file system access required - works within container constraints.
 */

const LOG_LEVELS = {
  ERROR: { level: 0, name: 'ERROR', emoji: '❌', console: 'error' },
  WARN: { level: 1, name: 'WARN', emoji: '⚠️', console: 'warn' },
  INFO: { level: 2, name: 'INFO', emoji: 'ℹ️', console: 'log' },
  DEBUG: { level: 3, name: 'DEBUG', emoji: '🔍', console: 'log' },
  TRACE: { level: 4, name: 'TRACE', emoji: '🔎', console: 'log' }
};

class SimpleLogger {
  constructor(category = 'APP', logLevel = 'INFO') {
    this.category = category;
    this.logLevel = LOG_LEVELS[logLevel] || LOG_LEVELS.INFO;
    this.startTime = Date.now();
  }

  /**
   * Create structured log entry that's easily queryable in Azure Log Analytics
   */
  _log(level, message, data = {}) {
    if (level.level > this.logLevel.level) {
      return; // Skip if below log level
    }

    const timestamp = new Date().toISOString();
    const uptime = Date.now() - this.startTime;

    const logEntry = {
      timestamp,
      level: level.name,
      category: this.category,
      message,
      uptime: `${uptime}ms`,
      ...data
    };

    // Format for console output (captured by Azure Log Analytics)
    const formatted = `${level.emoji} [${level.name}] [${this.category}] ${message}`;

    // Add structured data if provided
    if (Object.keys(data).length > 0) {
      console[level.console](formatted, JSON.stringify(logEntry));
    } else {
      console[level.console](formatted);
    }
  }

  error(message, data = {}) {
    this._log(LOG_LEVELS.ERROR, message, data);
  }

  warn(message, data = {}) {
    this._log(LOG_LEVELS.WARN, message, data);
  }

  info(message, data = {}) {
    this._log(LOG_LEVELS.INFO, message, data);
  }

  debug(message, data = {}) {
    this._log(LOG_LEVELS.DEBUG, message, data);
  }

  trace(message, data = {}) {
    this._log(LOG_LEVELS.TRACE, message, data);
  }

  /**
   * Log HTTP request
   */
  request(method, url, statusCode, duration, userId = null) {
    const data = {
      method,
      url,
      statusCode,
      duration: `${duration}ms`,
      userId
    };

    const level = statusCode >= 400 ? LOG_LEVELS.ERROR : LOG_LEVELS.INFO;
    this._log(level, `HTTP ${method} ${url} -> ${statusCode} (${duration}ms)`, data);
  }

  /**
   * Log authentication events
   */
  auth(action, userId, success = true, details = {}) {
    const level = success ? LOG_LEVELS.INFO : LOG_LEVELS.ERROR;
    const emoji = success ? '🔐' : '🚫';

    this._log(level, `${emoji} Auth ${action} for user ${userId}`, {
      action,
      userId,
      success,
      ...details
    });
  }

  /**
   * Log Twitch events
   */
  twitch(event, userId, details = {}) {
    this._log(LOG_LEVELS.INFO, `🎯 Twitch ${event} for user ${userId}`, {
      event,
      userId,
      ...details
    });
  }

  /**
   * Log database operations
   */
  database(operation, table, success = true, duration = null, details = {}) {
    const level = success ? LOG_LEVELS.DEBUG : LOG_LEVELS.ERROR;
    const emoji = success ? '💾' : '💥';

    const message = duration
      ? `${emoji} Database ${operation} on ${table} (${duration}ms)`
      : `${emoji} Database ${operation} on ${table}`;

    this._log(level, message, {
      operation,
      table,
      success,
      duration: duration ? `${duration}ms` : null,
      ...details
    });
  }

  /**
   * Log performance metrics
   */
  performance(operation, duration, details = {}) {
    const level = duration > 1000 ? LOG_LEVELS.WARN : LOG_LEVELS.DEBUG;

    this._log(level, `⏱️ Performance: ${operation} took ${duration}ms`, {
      operation,
      duration: `${duration}ms`,
      ...details
    });
  }

  /**
   * Create child logger with different category
   */
  child(category) {
    return new SimpleLogger(category, this.logLevel.name);
  }
}

// Create default loggers for different components
const createLogger = (category, logLevel = process.env.LOG_LEVEL || 'INFO') => {
  return new SimpleLogger(category, logLevel);
};

// Export default instances
const mainLogger = createLogger('MAIN');
const apiLogger = createLogger('API');
const authLogger = createLogger('AUTH');
const twitchLogger = createLogger('TWITCH');
const dbLogger = createLogger('DATABASE');

module.exports = {
  SimpleLogger,
  createLogger,
  mainLogger,
  apiLogger,
  authLogger,
  twitchLogger,
  dbLogger,
  LOG_LEVELS
};
