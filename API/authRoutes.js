const express = require('express');
const { RefreshingAuthProvider } = require('@twurple/auth');
const { ApiClient } = require('@twurple/api');
const jwt = require('jsonwebtoken');
const database = require('./database');
const keyVault = require('./keyVault');

const router = express.Router();

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
 * Step 1: Redirect user to Twitch OAuth authorization page
 * GET /auth/twitch
 */
router.get('/twitch', async (req, res) => {
  try {
    const clientId = await keyVault.getSecret('TWITCH-CLIENT-ID');
    const redirectUri = process.env.TWITCH_REDIRECT_URI || 'http://localhost:3000/auth/twitch/callback';

    // Scopes needed for the app
    const scopes = [
      'user:read:email',              // Read user email
      'chat:read',                    // Read chat messages
      'chat:edit',                    // Send chat messages
      'channel:read:subscriptions',   // Read subscriptions (EventSub cost reduction)
      'channel:read:redemptions',     // Read channel point redemptions (EventSub cost reduction)
      'moderator:read:followers',     // Read followers (EventSub cost reduction)
      'bits:read',                    // Read bits/cheers (EventSub cost reduction)
      'clips:edit'                    // Create clips (optional)
    ];

    const authUrl = `https://id.twitch.tv/oauth2/authorize?` +
      `client_id=${clientId}&` +
      `redirect_uri=${encodeURIComponent(redirectUri)}&` +
      `response_type=code&` +
      `scope=${encodeURIComponent(scopes.join(' '))}`;

    res.redirect(authUrl);
  } catch (error) {
    console.error('Error initiating OAuth:', error);
    res.status(500).json({ error: 'Failed to initiate authentication' });
  }
});

/**
 * Step 2: Handle OAuth callback from Twitch
 * GET /auth/twitch/callback?code=...
 */
router.get('/twitch/callback', async (req, res) => {
  try {
    const { code } = req.query;

    if (!code) {
      return res.status(400).json({ error: 'No authorization code provided' });
    }

    // Exchange code for access token
    const clientId = await keyVault.getSecret('TWITCH-CLIENT-ID');
    const clientSecret = await keyVault.getSecret('TWITCH-CLIENT-SECRET');
    const redirectUri = process.env.TWITCH_REDIRECT_URI || 'http://localhost:3000/auth/twitch/callback';

    const tokenResponse = await fetch('https://id.twitch.tv/oauth2/token', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded'
      },
      body: new URLSearchParams({
        client_id: clientId,
        client_secret: clientSecret,
        code: code,
        grant_type: 'authorization_code',
        redirect_uri: redirectUri
      })
    });

    const tokenData = await tokenResponse.json();

    if (!tokenResponse.ok) {
      console.error('Token exchange failed:', tokenData);
      return res.status(400).json({ error: 'Failed to exchange authorization code' });
    }

    // Get user information from Twitch
    const userResponse = await fetch('https://api.twitch.tv/helix/users', {
      headers: {
        'Authorization': `Bearer ${tokenData.access_token}`,
        'Client-Id': clientId
      }
    });

    const userData = await userResponse.json();
    const twitchUser = userData.data[0];

    // Calculate token expiry
    const tokenExpiry = new Date(Date.now() + tokenData.expires_in * 1000).toISOString();

    // Save user to database
    const user = await database.saveUser({
      twitchUserId: twitchUser.id,
      username: twitchUser.login,
      displayName: twitchUser.display_name,
      email: twitchUser.email,
      profileImageUrl: twitchUser.profile_image_url,
      accessToken: tokenData.access_token,
      refreshToken: tokenData.refresh_token,
      tokenExpiry: tokenExpiry
    });

    // Create JWT for our application
    const jwtSecret = await getJwtSecret();
    const jwtToken = jwt.sign(
      {
        userId: user.twitchUserId,
        username: user.username,
        displayName: user.displayName,
        role: user.role
      },
      jwtSecret,
      { expiresIn: '30d' }
    );

    // Redirect to frontend with token and role information
    const frontendUrl = process.env.FRONTEND_URL || 'http://localhost:5500';
    res.redirect(`${frontendUrl}?token=${jwtToken}&role=${user.role}`);

  } catch (error) {
    console.error('Error in OAuth callback:', error);
    res.status(500).json({ error: 'Authentication failed' });
  }
});

/**
 * Refresh Twitch access token
 * POST /auth/refresh
 */
router.post('/refresh', async (req, res) => {
  try {
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      return res.status(401).json({ error: 'No token provided' });
    }

    const token = authHeader.substring(7);
    const jwtSecret = await getJwtSecret();
    const decoded = jwt.verify(token, jwtSecret);

    // Get user from database
    const user = await database.getUser(decoded.userId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Check if token needs refresh (refresh if expires in less than 1 hour)
    const expiryDate = new Date(user.tokenExpiry);
    const needsRefresh = expiryDate - Date.now() < 3600000; // 1 hour in ms

    if (!needsRefresh) {
      return res.json({ message: 'Token still valid', expiresAt: user.tokenExpiry });
    }

    // Refresh the token
    const clientId = await keyVault.getSecret('TWITCH-CLIENT-ID');
    const clientSecret = await keyVault.getSecret('TWITCH-CLIENT-SECRET');

    const refreshResponse = await fetch('https://id.twitch.tv/oauth2/token', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded'
      },
      body: new URLSearchParams({
        client_id: clientId,
        client_secret: clientSecret,
        grant_type: 'refresh_token',
        refresh_token: user.refreshToken
      })
    });

    const refreshData = await refreshResponse.json();

    if (!refreshResponse.ok) {
      console.error('Token refresh failed:', refreshData);
      return res.status(400).json({ error: 'Failed to refresh token' });
    }

    // Update user tokens
    const newTokenExpiry = new Date(Date.now() + refreshData.expires_in * 1000).toISOString();
    await database.saveUser({
      ...user,
      accessToken: refreshData.access_token,
      refreshToken: refreshData.refresh_token,
      tokenExpiry: newTokenExpiry
    });

    res.json({
      message: 'Token refreshed successfully',
      expiresAt: newTokenExpiry
    });

  } catch (error) {
    console.error('Error refreshing token:', error);
    res.status(500).json({ error: 'Failed to refresh token' });
  }
});

/**
 * Get current user info
 * GET /auth/me
 */
router.get('/me', async (req, res) => {
  try {
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      return res.status(401).json({ error: 'No token provided' });
    }

    const token = authHeader.substring(7);
    const jwtSecret = await getJwtSecret();
    const decoded = jwt.verify(token, jwtSecret);

    const user = await database.getUser(decoded.userId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Don't send sensitive data
    res.json({
      userId: user.twitchUserId,
      username: user.username,
      displayName: user.displayName,
      email: user.email,
      profileImageUrl: user.profileImageUrl,
      lastLogin: user.lastLogin
    });

  } catch (error) {
    if (error.name === 'JsonWebTokenError' || error.name === 'TokenExpiredError') {
      return res.status(401).json({ error: 'Invalid or expired token' });
    }
    console.error('Error getting user info:', error);
    res.status(500).json({ error: 'Failed to get user info' });
  }
});

/**
 * Refresh JWT token using Twitch refresh token
 * POST /auth/refresh
 */
router.post('/refresh', async (req, res) => {
  try {
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      return res.status(401).json({ error: 'No token provided' });
    }

    const token = authHeader.substring(7);
    const jwtSecret = await getJwtSecret();

    let decoded;
    try {
      decoded = jwt.verify(token, jwtSecret);
    } catch (jwtError) {
      // Token might be expired, try to decode without verification to get user ID
      try {
        decoded = jwt.decode(token);
        if (!decoded || !decoded.userId) {
          return res.status(401).json({ error: 'Invalid token format' });
        }
      } catch (e) {
        return res.status(401).json({ error: 'Invalid token' });
      }
    }

    const user = await database.getUser(decoded.userId);
    if (!user || !user.refreshToken) {
      return res.status(401).json({ error: 'User not found or no refresh token' });
    }

    try {
      const clientId = await keyVault.getSecret('TWITCH-CLIENT-ID');
      const clientSecret = await keyVault.getSecret('TWITCH-CLIENT-SECRET');

      // Use Twitch refresh token to get new access token
      const refreshResponse = await fetch('https://id.twitch.tv/oauth2/token', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded',
        },
        body: new URLSearchParams({
          grant_type: 'refresh_token',
          refresh_token: user.refreshToken,
          client_id: clientId,
          client_secret: clientSecret
        })
      });

      if (!refreshResponse.ok) {
        console.error('❌ Twitch refresh failed:', refreshResponse.status);
        return res.status(401).json({ error: 'Failed to refresh Twitch token' });
      }

      const refreshData = await refreshResponse.json();

      // Update user with new tokens
      await database.updateUser(user.twitchUserId, {
        accessToken: refreshData.access_token,
        refreshToken: refreshData.refresh_token,
        tokenExpiry: new Date(Date.now() + refreshData.expires_in * 1000)
      });

      // Generate new JWT with fresh expiration
      const newJwtPayload = {
        userId: user.twitchUserId,
        username: user.username,
        role: user.role
      };

      const newJwtToken = jwt.sign(newJwtPayload, jwtSecret, { expiresIn: '30d' });

      console.log(`✅ Token refreshed for user: ${user.username}`);
      res.json({
        token: newJwtToken,
        user: {
          twitchUserId: user.twitchUserId,
          username: user.username,
          displayName: user.displayName,
          role: user.role
        }
      });

    } catch (error) {
      console.error('❌ Error refreshing token:', error);
      res.status(401).json({ error: 'Failed to refresh token' });
    }

  } catch (error) {
    console.error('❌ Error in refresh endpoint:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
});

/**
 * Refresh JWT token
 * POST /auth/refresh-jwt
 */
router.post('/refresh-jwt', async (req, res) => {
  try {
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      return res.status(401).json({ error: 'No token provided' });
    }

    const token = authHeader.substring(7);
    const jwtSecret = await getJwtSecret();

    let decoded;
    try {
      // Try to verify token first
      decoded = jwt.verify(token, jwtSecret);
    } catch (error) {
      if (error.name === 'TokenExpiredError') {
        // Decode without verification for expired tokens
        decoded = jwt.decode(token);
      } else {
        return res.status(401).json({ error: 'Invalid token' });
      }
    }

    if (!decoded || !decoded.userId) {
      return res.status(401).json({ error: 'Invalid token format' });
    }

    // Get user from database
    const user = await database.getUser(decoded.userId);
    if (!user) {
      return res.status(404).json({ error: 'User not found' });
    }

    // Check if user's account is still active
    if (user.isActive === false) {
      return res.status(403).json({ error: 'Account disabled' });
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

    console.log(`🔄 JWT token manually refreshed for user: ${user.username}`);

    res.json({
      token: newJwtToken,
      user: {
        userId: user.twitchUserId,
        username: user.username,
        displayName: user.displayName,
        role: user.role
      },
      expiresIn: '30d'
    });

  } catch (error) {
    console.error('Error refreshing JWT token:', error);
    res.status(500).json({ error: 'Failed to refresh token' });
  }
});

/**
 * Logout (invalidate session)
 * POST /auth/logout
 */
router.post('/logout', async (req, res) => {
  try {
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      return res.status(401).json({ error: 'No token provided' });
    }

    // In a production app, you might want to:
    // 1. Add token to a blacklist
    // 2. Revoke the Twitch token
    // For now, client-side deletion is sufficient

    res.json({ message: 'Logged out successfully' });

  } catch (error) {
    console.error('Error logging out:', error);
    res.status(500).json({ error: 'Failed to logout' });
  }
});

module.exports = router;
