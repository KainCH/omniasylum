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
    console.log('🔐 requireAuth - Headers received:', Object.keys(req.headers));
    console.log('🔐 requireAuth - Authorization header:', req.headers.authorization ? 'EXISTS' : 'MISSING');

    const authHeader = req.headers.authorization;

    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      console.log('🔐 requireAuth - FAILED: No Bearer token found');
      return res.status(401).json({ error: 'Authentication required' });
    }

    const token = authHeader.substring(7);
    console.log('🔐 requireAuth - Token extracted:', token ? 'EXISTS' : 'MISSING');

    try {
      const jwtSecret = await getJwtSecret();
      const decoded = jwt.verify(token, jwtSecret);
      console.log('🔐 requireAuth - Token decoded successfully:', decoded.username);

      // Get user from database to ensure they still exist and get latest data
      const user = await database.getUser(decoded.userId);

      if (!user) {
        console.log('🔐 requireAuth - FAILED: User not found in database:', decoded.userId);
        return res.status(401).json({ error: 'User not found' });
      }

      console.log('🔐 requireAuth - SUCCESS: User authenticated:', user.username);

      // Attach user to request
      req.user = {
        userId: user.twitchUserId,
        username: user.username,
        displayName: user.displayName,
        accessToken: user.accessToken,
        refreshToken: user.refreshToken,
        role: user.role
      };

      next();
    } catch (error) {
      console.log('🔐 requireAuth - JWT Error:', error.name, error.message);
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
    console.log('🔌 Socket Auth - Connection attempt from:', socket.request.connection.remoteAddress);
    console.log('🔌 Socket Auth - Handshake auth:', socket.handshake.auth ? 'EXISTS' : 'MISSING');

    const token = socket.handshake.auth.token;

    if (!token) {
      console.log('⚠️  Socket Auth - No token provided (allowing for overlay)');
      // Allow unauthenticated connections (for overlay pages)
      // They will need to call joinRoom with a userId
      socket.userId = null;
      socket.username = 'unauthenticated';
      socket.displayName = 'Overlay';
      return next();
    }

    console.log('🔌 Socket Auth - Token received:', token ? 'EXISTS' : 'MISSING');

    try {
      const jwtSecret = await getJwtSecret();
      const decoded = jwt.verify(token, jwtSecret);
      console.log('✅ Socket Auth - Token decoded successfully:', decoded.username);

      // Attach user info to socket
      socket.userId = decoded.userId;
      socket.username = decoded.username;
      socket.displayName = decoded.displayName;

      console.log('✅ Socket Auth - User authenticated:', decoded.username);
      next();
    } catch (error) {
      console.log('❌ Socket Auth - Token verification failed:', error.message);
      return next(new Error('Invalid token'));
    }
  } catch (error) {
    console.log('❌ Socket Auth - Authentication failed:', error.message);
    return next(new Error('Authentication failed'));
  }
}

/**
 * Middleware to verify user is an admin
 * Note: This middleware expects that requireAuth has already been applied
 */
async function requireAdmin(req, res, next) {
  try {
    console.log('🔐 requireAdmin - User object:', req.user ? 'EXISTS' : 'MISSING');

    // Check if user is already authenticated (should be set by requireAuth middleware)
    if (!req.user || !req.user.userId) {
      console.log('🔐 requireAdmin - FAILED: No user object from requireAuth');
      return res.status(401).json({ error: 'Authentication required' });
    }

    console.log('🔐 requireAdmin - Checking admin role for user:', req.user.username);

    // Check if user is admin
    const user = await database.getUser(req.user.userId);

    if (!user || user.role !== 'admin') {
      console.log('🔐 requireAdmin - FAILED: User role is', user?.role, 'not admin');
      return res.status(403).json({
        error: 'Forbidden',
        message: 'Admin access required'
      });
    }

    console.log('🔐 requireAdmin - SUCCESS: Admin access granted');
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
