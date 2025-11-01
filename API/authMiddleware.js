const jwt = require('jsonwebtoken');
const database = require('./database');

const JWT_SECRET = process.env.JWT_SECRET || 'your-secret-key-change-in-production';

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
      const decoded = jwt.verify(token, JWT_SECRET);
      
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
        const decoded = jwt.verify(token, JWT_SECRET);
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
function verifySocketAuth(socket, next) {
  try {
    const token = socket.handshake.auth.token;
    
    if (!token) {
      return next(new Error('Authentication required'));
    }

    try {
      const decoded = jwt.verify(token, JWT_SECRET);
      
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

module.exports = {
  requireAuth,
  optionalAuth,
  verifySocketAuth
};
