const jwt = require('jsonwebtoken');
const database = require('./database');
const keyVault = require('./keyVault');

// JWT secret from Key Vault
let JWT_SECRET = null;

// Initialize JWT secret from Key Vault
async function getJwtSecret() {
  if (!JWT_SECRET) {
    JWT_SECRET = await keyVault.getSecret('JWT-SECRET');
  }
  return JWT_SECRET;
}

/**
 * Middleware to verify JWT token and attach user to request
 */
async function requireAuth(req, res, next) {
  try {
    const authHeader = req.headers.authorization;

    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      return res.status(401).json({ error: 'Authentication required' });
    }

    const token = authHeader.substring(7);

    try {
      const jwtSecret = await getJwtSecret();
      const decoded = jwt.verify(token, jwtSecret);

      // Get user from database to ensure they still exist and get latest data
      const user = await database.getUser(decoded.userId);

      if (!user) {
        return res.status(401).json({ error: 'User not found' });
      }

      // Attach user to request
      req.user = {
        userId: user.twitchUserId,
        username: user.username,
        displayName: user.displayName,
        accessToken: user.accessToken,
        refreshToken: user.refreshToken
      };

      next();
    } catch (error) {
      if (error.name === 'JsonWebTokenError') {
        return res.status(401).json({ error: 'Invalid token' });
      }
      if (error.name === 'TokenExpiredError') {
        return res.status(401).json({ error: 'Token expired' });
      }
      throw error;
    }
  } catch (error) {
    console.error('Auth middleware error:', error);
    res.status(500).json({ error: 'Authentication failed' });
  }
}

/**
 * Optional auth - attaches user if token present but doesn't require it
 */
async function optionalAuth(req, res, next) {
  try {
    const authHeader = req.headers.authorization;

    if (authHeader && authHeader.startsWith('Bearer ')) {
      const token = authHeader.substring(7);

      try {
        const jwtSecret = await getJwtSecret();
        const decoded = jwt.verify(token, jwtSecret);
        const user = await database.getUser(decoded.userId);

        if (user) {
          req.user = {
            userId: user.twitchUserId,
            username: user.username,
            displayName: user.displayName,
            accessToken: user.accessToken,
            refreshToken: user.refreshToken
          };
        }
      } catch (error) {
        // Silently fail for optional auth
        console.log('Optional auth failed:', error.message);
      }
    }

    next();
  } catch (error) {
    console.error('Optional auth middleware error:', error);
    next(); // Continue even if error
  }
}

/**
 * Middleware to verify WebSocket authentication
 */
async function verifySocketAuth(socket, next) {
  try {
    const token = socket.handshake.auth.token;

    if (!token) {
      return next(new Error('Authentication required'));
    }

    try {
      const jwtSecret = await getJwtSecret();
      const decoded = jwt.verify(token, jwtSecret);

      // Attach user info to socket
      socket.userId = decoded.userId;
      socket.username = decoded.username;
      socket.displayName = decoded.displayName;

      next();
    } catch (error) {
      return next(new Error('Invalid token'));
    }
  } catch (error) {
    return next(new Error('Authentication failed'));
  }
}

/**
 * Middleware to verify user is an admin
 * Note: This middleware expects that requireAuth has already been applied
 */
async function requireAdmin(req, res, next) {
  try {
    // Check if user is already authenticated (should be set by requireAuth middleware)
    if (!req.user || !req.user.userId) {
      return res.status(401).json({ error: 'Authentication required' });
    }

    // Check if user is admin
    const user = await database.getUser(req.user.userId);

    if (!user || user.role !== 'admin') {
      return res.status(403).json({
        error: 'Forbidden',
        message: 'Admin access required'
      });
    }

    next();
  } catch (error) {
    console.error('Admin middleware error:', error);
    res.status(500).json({ error: 'Authorization failed' });
  }
}

module.exports = {
  requireAuth,
  optionalAuth,
  verifySocketAuth,
  requireAdmin
};
