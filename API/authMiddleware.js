const jwt = require('jsonwebtoken');
const database = require('./database');
const keyVault = require('./keyVault');
const { authLogger } = require('./logger');

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
 * Attempt to refresh expired JWT token
 */
async function attemptJwtRefresh(req, res, next, expiredToken) {
  try {
    console.log('🔄 Attempting JWT token refresh...');

    // Decode expired token without verification to get user info
    const jwtSecret = await getJwtSecret();
    const decoded = jwt.decode(expiredToken);

    if (!decoded || !decoded.userId) {
      console.log('🔐 JWT Refresh - FAILED: Cannot decode expired token');
      return res.status(401).json({ error: 'Token expired and cannot be refreshed' });
    }

    // Get user from database
    const user = await database.getUser(decoded.userId);
    if (!user) {
      console.log('🔐 JWT Refresh - FAILED: User not found:', decoded.userId);
      return res.status(401).json({ error: 'User not found' });
    }

    // Check if user's Twitch token is still valid (or can be refreshed)
    const tokenExpiry = new Date(user.tokenExpiry);
    const now = new Date();

    // If Twitch token expired more than 60 days ago, user needs to re-authenticate
    if (tokenExpiry < new Date(now - 60 * 24 * 60 * 60 * 1000)) {
      console.log('🔐 JWT Refresh - FAILED: Twitch token too old, re-auth required');
      return res.status(401).json({
        error: 'Authentication expired',
        requireReauth: true,
        authUrl: '/auth/twitch'
      });
    }

    // Generate new JWT token
    const newJwtToken = jwt.sign(
      {
        userId: user.twitchUserId,
        username: user.username,
        displayName: user.displayName,
        role: user.role
      },
      jwtSecret,
      { expiresIn: '30d' }
    );

    console.log(`🔄 JWT token refreshed for user: ${user.username}`);

    // Set the new token in response header
    res.setHeader('X-New-Token', newJwtToken);
    res.setHeader('X-Token-Refreshed', 'true');

    // Attach user to request and continue
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
    console.error('🔐 JWT Refresh - Error:', error);
    return res.status(401).json({ error: 'Token expired and refresh failed' });
  }
}

/**
 * Middleware to verify JWT token and attach user to request
 */
async function requireAuth(req, res, next) {
  try {
    authLogger.debug('Processing authentication request', {
      method: req.method,
      url: req.url,
      hasAuthHeader: !!req.headers.authorization
    });

    const authHeader = req.headers.authorization;

    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      authLogger.warn('Authentication failed - no Bearer token', {
        method: req.method,
        url: req.url,
        hasHeader: !!authHeader
      });
      return res.status(401).json({ error: 'Authentication required' });
    }

    const token = authHeader.substring(7);
    authLogger.debug('Bearer token extracted for verification');

    try {
      const jwtSecret = await getJwtSecret();
      const decoded = jwt.verify(token, jwtSecret);
      authLogger.auth('token-verify', decoded.userId, true, { username: decoded.username });

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
        // Attempt to refresh JWT token automatically
        return await attemptJwtRefresh(req, res, next, token);
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
