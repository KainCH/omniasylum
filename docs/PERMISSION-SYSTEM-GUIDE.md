# 🔑 Permission System Guide - OmniAsylum Stream Counter

## Overview

The OmniAsylum Stream Counter now features a comprehensive **three-tier role-based permission system** that allows delegation of configuration tasks to trusted users. This enables mod team members to configure stream settings before broadcasts go live, eliminating last-minute setup stress for streamers.

## 🎯 Role Hierarchy

### 1. **Super Admin** (`super_admin`)
- **User**: `riress` (automatically assigned)
- **Capabilities**:
  - Full system access and control
  - Grant/revoke manager permissions to users for specific broadcasters
  - Manage all users and their settings
  - View system-wide statistics and debug information
  - Override any permission restrictions

### 2. **Manager** (`manager`)
- **Assignment**: Granted by Super Admin for specific broadcasters
- **Capabilities**:
  - Configure settings for assigned broadcasters only
  - Manage overlays, features, alerts, and rewards for assigned users
  - Cannot access users they're not assigned to manage
  - Cannot grant permissions to others
  - Limited to configuration tasks, not system administration

### 3. **Streamer** (`streamer`)
- **Assignment**: Default role for all users
- **Capabilities**:
  - Full control over their own stream settings
  - Cannot access other users' configurations
  - Cannot grant permissions to others
  - Standard user functionality

## 🚀 Getting Started

### For Super Admin (riress)

1. **Access Permission Management**:
   - Login to the admin portal
   - Navigate to Admin Dashboard
   - Click **🔑 Manage Permissions** button

2. **Grant Manager Permissions**:
   ```
   1. Select a user from the dropdown
   2. Choose the broadcaster they should manage
   3. Click "Grant Manager Permissions"
   4. User will immediately gain access to configure that broadcaster's settings
   ```

3. **Revoke Permissions**:
   ```
   1. Find the manager in the "Current Managers" section
   2. Click "Revoke" next to the broadcaster they manage
   3. Access is immediately removed
   ```

### For Managers

1. **Access Management Interface**:
   - Login with your Twitch account
   - You'll see only the broadcasters you're assigned to manage
   - Navigate to the specific broadcaster's configuration

2. **Available Configuration Options**:
   - **Feature Flags**: Enable/disable chat commands, channel points, etc.
   - **Overlay Settings**: Customize counter displays and themes
   - **Alert Management**: Configure death/swear alerts and effects
   - **Reward Management**: Set up channel point redemptions
   - **Stream Status**: Enable/disable user accounts

3. **Limitations**:
   - Cannot access users you're not assigned to
   - Cannot grant permissions to others
   - Cannot view system-wide statistics
   - Cannot access debug information

## 🛠️ Technical Implementation

### Database Schema

```javascript
// Users table enhanced with role system
{
  twitchUserId: string,           // Partition key
  username: string,
  displayName: string,
  role: 'super_admin' | 'manager' | 'streamer',
  managedStreamers: [             // Array of twitchUserIds this manager can access
    "123456789",
    "987654321"
  ],
  // ... other user properties
}
```

### API Endpoints

#### Permission Management (Super Admin Only)
```javascript
POST /api/permissions/grant-manager
POST /api/permissions/revoke-manager
GET  /api/permissions/managed-users/:userId
GET  /api/permissions/can-manage/:managerId/:streamerId
GET  /api/permissions/all-users-roles
```

#### Manager Configuration Access
```javascript
GET    /api/admin/manage/:userId                    // Get user details
PUT    /api/admin/manage/:userId                    // Update user settings
GET    /api/admin/manage/:userId/features           // Get feature flags
PUT    /api/admin/manage/:userId/features           // Update feature flags
GET    /api/admin/manage/:userId/overlay            // Get overlay settings
PUT    /api/admin/manage/:userId/overlay            // Update overlay settings
PUT    /api/admin/manage/:userId/status             // Enable/disable user
```

### Middleware Protection

```javascript
// Example endpoint protection
app.use('/api/admin/manage/:userId',
  requireAuth,                    // Must be logged in
  requireManagerAccess,           // Must be manager for this user OR super_admin
  managerRoutes
);

app.use('/api/permissions/*',
  requireAuth,                    // Must be logged in
  requireSuperAdmin,              // Must be super_admin role
  permissionRoutes
);
```

## 🎭 Use Cases

### Scenario 1: Pre-Stream Setup
**Problem**: Streamer needs overlay configured but is busy with pre-stream preparations.

**Solution**:
1. Super Admin grants manager permissions to trusted mod
2. Manager logs in and configures overlay settings
3. Manager sets up alerts and channel point rewards
4. Streamer goes live with everything ready

### Scenario 2: Multi-Streamer Team
**Problem**: Gaming team has multiple streamers who need similar configurations.

**Solution**:
1. Super Admin assigns one manager per streamer
2. Each manager configures their assigned streamer's settings
3. Managers can prepare configurations in advance
4. Streamers focus on content creation, not technical setup

### Scenario 3: Mod Team Delegation
**Problem**: Head mod wants to delegate configuration tasks to assistant mods.

**Solution**:
1. Super Admin grants manager permissions to head mod
2. Head mod handles main configurations
3. Other team members get manager access for specific tasks
4. Distributed workload with proper access control

## 🔒 Security Features

### Role Validation
- All API endpoints validate user roles server-side
- Client-side role checks are never trusted
- JWT tokens contain role information for quick validation

### Access Isolation
- Managers can only access users they're explicitly assigned to
- Database queries automatically filter by user permissions
- No cross-tenant data leakage possible

### Permission Revocation
- Immediate effect when permissions are revoked
- No cached permissions or delayed updates
- Real-time access control validation

## 🧪 Testing the System

### Super Admin Testing
1. Login as `riress`
2. Verify permission management interface appears
3. Grant manager permissions to test user
4. Verify test user gains access to assigned broadcaster
5. Revoke permissions and verify access is removed

### Manager Testing
1. Login as user with manager permissions
2. Verify only assigned broadcasters are visible
3. Test configuration access for assigned users
4. Verify denied access to non-assigned users
5. Confirm no permission management interface appears

### Streamer Testing
1. Login as regular streamer
2. Verify own settings are accessible
3. Verify no access to other users' settings
4. Confirm no admin or manager interfaces appear

## 🚨 Troubleshooting

### Common Issues

#### Manager Can't Access Assigned User
**Check**:
1. Verify manager permissions were granted correctly
2. Check that user exists and is active
3. Confirm manager is logged in with correct account
4. Verify JWT token contains updated role information

#### Permission Changes Not Taking Effect
**Solution**:
1. User may need to logout and login again to refresh JWT
2. Check server logs for authorization errors
3. Verify database permissions were updated correctly

#### Super Admin Can't Grant Permissions
**Check**:
1. Confirm user is logged in as `riress`
2. Verify super_admin role in database
3. Check that target users exist in the system
4. Review browser console for API errors

### Debug Commands

```javascript
// Check user permissions in database
GET /api/permissions/all-users-roles

// Verify manager access for specific user
GET /api/permissions/can-manage/:managerId/:streamerId

// View managed users for a manager
GET /api/permissions/managed-users/:managerId
```

## 📈 Future Enhancements

### Planned Features
- **Time-Limited Permissions**: Grant manager access for specific time periods
- **Granular Permissions**: Allow different levels of manager access (read-only, configuration-only, etc.)
- **Audit Logs**: Track all permission changes and configuration updates
- **Bulk Operations**: Grant/revoke permissions for multiple users at once
- **Permission Templates**: Pre-defined permission sets for common scenarios

### Extension Points
- **Custom Roles**: Define additional roles beyond the current three-tier system
- **Permission Inheritance**: Hierarchical permission structures
- **External Integration**: API access for permission management from external tools
- **Notification System**: Alert users when permissions are granted/revoked

## 📞 Support

For issues with the permission system:
1. Check this guide for common solutions
2. Review server logs for detailed error messages
3. Test with different user accounts to isolate the issue
4. Contact system administrator (riress) for permission-related problems

## 🔄 Migration Notes

### From Previous System
- Old 'admin' role automatically converted to 'super_admin'
- All existing users default to 'streamer' role
- No existing manager permissions (must be granted explicitly)
- Backward compatibility maintained for all existing features

### Database Updates Required
- `role` field updated from 'admin' to 'super_admin' for riress
- `managedStreamers` array added to users table
- New permission management endpoints activated
- Enhanced middleware deployed with role checking

---

**Last Updated**: December 2024
**Version**: 2.0
**System**: OmniAsylum Stream Counter Multi-Tenant Architecture
