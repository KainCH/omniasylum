/**
 * Express middleware for request logging
 *
 * Automatically logs all HTTP requests with timing and response codes
 * Compatible with Azure Container Apps - uses console output only
 */

const { apiLogger } = require('./logger');

/**
 * Request logging middleware
 */
const requestLogger = (req, res, next) => {
  const startTime = Date.now();
  const originalSend = res.send;
  const originalJson = res.json;

  // Track request details
  const requestId = Math.random().toString(36).substring(2, 8);
  const method = req.method;
  const url = req.originalUrl || req.url;
  const userAgent = req.get('User-Agent') || 'Unknown';
  const ip = req.ip || req.connection?.remoteAddress || 'Unknown';
  const userId = req.user?.userId || null;

  // Log request start
  apiLogger.info(`→ ${method} ${url}`, {
    requestId,
    method,
    url,
    userAgent: userAgent.substring(0, 100), // Truncate long user agents
    ip,
    userId
  });

  // Override res.send to capture response
  res.send = function(body) {
    const duration = Date.now() - startTime;
    const statusCode = res.statusCode;
    const finalUserId = req.user?.userId || null; // Capture userId at response time

    // Log response
    apiLogger.request(method, url, statusCode, duration, finalUserId);

    // Call original send
    originalSend.call(this, body);
  };

  // Override res.json to capture JSON responses
  res.json = function(obj) {
    const duration = Date.now() - startTime;
    const statusCode = res.statusCode;
    const finalUserId = req.user?.userId || null; // Capture userId at response time

    // Log response with JSON indicator
    apiLogger.request(method, url, statusCode, duration, finalUserId);

    // Call original json
    originalJson.call(this, obj);
  };

  // Handle response finish event (fallback)
  res.on('finish', () => {
    const duration = Date.now() - startTime;
    const statusCode = res.statusCode;
    const finalUserId = req.user?.userId || null; // Capture userId at response time

    // Only log if we haven't already logged this request
    if (!res.headersSent || statusCode === 0) {
      apiLogger.request(method, url, statusCode, duration, finalUserId);
    }
  });

  next();
};

/**
 * Error logging middleware (should be used after routes)
 */
const errorLogger = (err, req, res, next) => {
  const method = req.method;
  const url = req.originalUrl || req.url;
  const userId = req.user?.userId || null;

  // Log the error
  apiLogger.error(`💥 Unhandled error in ${method} ${url}`, {
    error: err.message,
    stack: err.stack,
    method,
    url,
    userId
  });

  // Pass error to next middleware
  next(err);
};

/**
 * Performance monitoring middleware for slow requests
 */
const performanceLogger = (threshold = 1000) => {
  return (req, res, next) => {
    const startTime = Date.now();

    res.on('finish', () => {
      const duration = Date.now() - startTime;

      if (duration > threshold) {
        const method = req.method;
        const url = req.originalUrl || req.url;
        const userId = req.user?.userId || null;

        apiLogger.performance(`Slow request: ${method} ${url}`, duration, {
          method,
          url,
          userId,
          statusCode: res.statusCode
        });
      }
    });

    next();
  };
};

module.exports = {
  requestLogger,
  errorLogger,
  performanceLogger
};
