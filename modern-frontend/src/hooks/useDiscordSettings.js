import { useState, useEffect, useCallback } from 'react'
import { useToast, useLoading } from './index'
import {
  getDiscordSettingsUnified,
  getDiscordWebhookUnified,
  getDiscordInviteUnified,
  updateDiscordSettingsUnified,
  userAPI,
  adminAPI,
  APIError
} from '../utils/authUtils'
import {
  createDefaultNotificationSettings,
  validateNotificationSettings,
  parseThresholdString
} from '../utils/notificationHelpers'

export function useDiscordSettings(user) {
  const { showToast } = useToast()
  const { isLoading, withLoading } = useLoading()

  // Core state
  const [webhookData, setWebhookData] = useState({
    webhookUrl: '',
    enabled: false
  })

  const [notificationSettings, setNotificationSettings] = useState({
    enableChannelNotifications: false,
    deathMilestoneEnabled: true,
    swearMilestoneEnabled: true,
    deathThresholds: '10,25,50,100,250,500',
    swearThresholds: '25,50,100,200,500'
  })

  const [discordInvite, setDiscordInvite] = useState('')
  const [message, setMessage] = useState('')
  const [messageType, setMessageType] = useState('')

  // Helper function to get the correct user ID
  const getUserId = useCallback(() => {
    return user?.twitchUserId || user?.userId
  }, [user])

  // Check if we're in admin mode
  const isAdminMode = useCallback(() => {
    return user?.role === 'admin' && window.location.pathname.includes('/admin')
  }, [user])

  // Show message helper
  const showMessage = useCallback((text, type) => {
    setMessage(text)
    setMessageType(type)
    setTimeout(() => setMessage(''), 3000)
  }, [])

  // Update notification setting helper
  const updateNotificationSetting = useCallback((key, value) => {
    console.log('🔧 updateNotificationSetting called:', { key, value })
    setNotificationSettings(prevSettings => {
      const updatedSettings = {
        ...prevSettings,
        [key]: value
      }
      console.log('📝 Updated notification settings:', updatedSettings)
      return updatedSettings
    })
  }, [])

  // Update webhook data helper
  const updateWebhookData = useCallback((key, value) => {
    setWebhookData(prevData => ({
      ...prevData,
      [key]: value
    }))
  }, [])

  // Validate settings
  const validateSettings = useCallback(() => {
    return validateNotificationSettings(notificationSettings)
  }, [notificationSettings])

  // Reset to defaults
  const resetToDefaults = useCallback((newSettings = null) => {
    if (newSettings) {
      setNotificationSettings(newSettings)
    } else {
      setNotificationSettings({
        enableChannelNotifications: false,
        deathMilestoneEnabled: true,
        swearMilestoneEnabled: true,
        deathThresholds: '10,25,50,100,250,500',
        swearThresholds: '25,50,100,200,500'
      })
    }
  }, [])

  // Load user Discord settings
  const loadUserDiscordSettings = useCallback(async () => {
    console.log('📥 Starting to load Discord settings...')
    const adminMode = isAdminMode()
    console.log('🔍 Loading settings in admin mode:', adminMode)

    try {
      await withLoading(async () => {
        // Load webhook URL
        console.log('🔗 Loading webhook URL...')
        const webhookData = await getDiscordWebhookUnified(user)
        console.log('🔗 Webhook data received:', webhookData)

        if (webhookData) {
          setWebhookData({
            webhookUrl: webhookData.webhookUrl || '',
            enabled: webhookData.enabled || false
          })
        }

        // Load notification settings
        console.log('🔔 Loading notification settings...')
        const settingsData = await getDiscordSettingsUnified(user)
        console.log('🔔 Settings data received:', settingsData)

        if (settingsData) {
          const safeSettings = {
            enableChannelNotifications: settingsData.enableChannelNotifications || false,
            deathMilestoneEnabled: settingsData.deathMilestoneEnabled !== undefined ? settingsData.deathMilestoneEnabled : true,
            swearMilestoneEnabled: settingsData.swearMilestoneEnabled !== undefined ? settingsData.swearMilestoneEnabled : true,
            deathThresholds: settingsData.deathThresholds || '10,25,50,100,250,500',
            swearThresholds: settingsData.swearThresholds || '25,50,100,200,500'
          }

          console.log('🔔 Applying safe settings:', safeSettings)
          setNotificationSettings(safeSettings)
        }
      })

      console.log('✅ Discord settings loaded successfully')
      showMessage('Discord settings loaded successfully', 'success')
    } catch (error) {
      console.error('❌ Failed to load Discord settings:', error)
      showMessage('Failed to load Discord settings', 'error')
    }
  }, [user, isAdminMode, withLoading, showMessage])

  // Load Discord invite
  const loadDiscordInvite = useCallback(async () => {
    try {
      console.log('🔗 Loading Discord invite...')
      const invite = await getDiscordInviteUnified(user)
      console.log('🔗 Discord invite received:', invite)
      // Backend returns 'discordInviteLink', not 'inviteUrl'
      setDiscordInvite(invite?.discordInviteLink || invite?.inviteUrl || '')
    } catch (error) {
      console.error('❌ Failed to load Discord invite:', error)
    }
  }, [user])

  // Load all Discord data
  const loadAllDiscordData = useCallback(async () => {
    console.log('📥 Starting to load ALL Discord data using unified functions...')
    console.log('👤 Target user:', user)

    try {
      await withLoading(async () => {
        // Load Discord settings (includes webhook URL and notification settings)
        try {
          console.log('📊 Loading Discord settings using unified function...')
          const discordData = await getDiscordSettingsUnified(user)

          console.log('🔗 RAW Complete Discord data received:', discordData)

          if (discordData) {
            // Set webhook data
            setWebhookData({
              webhookUrl: discordData.webhookUrl || '',
              enabled: discordData.enabled || false
            })

            // Set notification settings with safe defaults
            const safeNotificationSettings = {
              enableChannelNotifications: discordData.enableChannelNotifications || false,
              deathMilestoneEnabled: discordData.deathMilestoneEnabled !== undefined ? discordData.deathMilestoneEnabled : true,
              swearMilestoneEnabled: discordData.swearMilestoneEnabled !== undefined ? discordData.swearMilestoneEnabled : true,
              deathThresholds: discordData.deathThresholds || '10,25,50,100,250,500',
              swearThresholds: discordData.swearThresholds || '25,50,100,200,500'
            }

            console.log('🔔 Setting notification settings:', safeNotificationSettings)
            setNotificationSettings(safeNotificationSettings)
          } else {
            console.log('⚠️ No Discord data found, using defaults')
            resetToDefaults()
          }
        } catch (error) {
          console.error('❌ Error loading Discord settings:', error)
          resetToDefaults()
        }

        // Load Discord invite separately
        try {
          await loadDiscordInvite()
        } catch (error) {
          console.error('❌ Error loading Discord invite:', error)
        }
      })

      console.log('✅ All Discord data loaded successfully')
      showMessage('Discord data loaded successfully', 'success')
    } catch (error) {
      console.error('❌ Failed to load Discord data:', error)
      showMessage('Failed to load Discord data', 'error')
    }
  }, [user, withLoading, showMessage, loadDiscordInvite, resetToDefaults])

  // Save Discord settings
  const saveDiscordSettings = useCallback(async () => {
    if (!user) {
      showMessage('No user data available', 'error')
      return false
    }

    try {
      const validation = validateSettings()
      if (!validation.isValid) {
        showMessage(`Invalid settings: ${validation.errors.join(', ')}`, 'error')
        return false
      }

      await withLoading(async () => {
        const settingsToSave = {
          ...webhookData,
          ...notificationSettings
        }

        console.log('💾 Saving Discord settings:', settingsToSave)
        await updateDiscordSettingsUnified(user, settingsToSave)
      })

      console.log('✅ Discord settings saved successfully')
      showMessage('Discord settings saved successfully', 'success')
      return true
    } catch (error) {
      console.error('❌ Failed to save Discord settings:', error)
      showMessage('Failed to save Discord settings', 'error')
      return false
    }
  }, [user, webhookData, notificationSettings, validateSettings, withLoading, showMessage])

  // Test Discord webhook
  const testDiscordWebhook = useCallback(async () => {
    if (!webhookData.webhookUrl) {
      showMessage('Please enter a webhook URL first', 'error')
      return false
    }

    try {
      await withLoading(async () => {
        const adminMode = isAdminMode()
        const api = adminMode ? adminAPI : userAPI
        await api.testDiscordWebhook(getUserId(), {
          webhookUrl: webhookData.webhookUrl,
          message: 'Test message from OmniAsylum Stream Counter! 🎮'
        })
      })

      console.log('✅ Discord webhook test successful')
      showMessage('Test message sent successfully! Check your Discord channel.', 'success')
      return true
    } catch (error) {
      console.error('❌ Discord webhook test failed:', error)
      showMessage('Failed to send test message', 'error')
      return false
    }
  }, [webhookData.webhookUrl, isAdminMode, getUserId, withLoading, showMessage])

  // Load data when user changes
  useEffect(() => {
    if (user) {
      console.log('🎬 User changed, loading Discord data')
      loadAllDiscordData()
    }
  }, [user, loadAllDiscordData])

  // Update Discord invite
  const updateDiscordInvite = useCallback(async (inviteUrl) => {
    return withLoading(async () => {
      try {
        let response
        const userId = getUserId()

        if (isAdminMode()) {
          response = await adminAPI.updateUserDiscordInvite(userId, { discordInviteLink: inviteUrl })
        } else {
          response = await userAPI.updateDiscordInvite({ discordInviteLink: inviteUrl })
        }

        if (response.message || response.discordInviteLink !== undefined) {
          setDiscordInvite(inviteUrl)
          showMessage('Discord invite link updated successfully!', 'success')
          return true
        } else {
          throw new Error(response.error || 'Failed to update Discord invite')
        }
      } catch (error) {
        console.error('❌ Error updating Discord invite:', error)
        if (error instanceof APIError) {
          showMessage(error.userMessage, 'error')
        } else {
          showMessage('Failed to update Discord invite link', 'error')
        }
        return false
      }
    })
  }, [getUserId, isAdminMode, withLoading, showMessage])

  return {
    // State
    webhookData,
    notificationSettings,
    discordInvite,
    message,
    messageType,
    isLoading,

    // Actions
    updateNotificationSetting,
    updateWebhookData,
    updateDiscordInvite,
    loadAllDiscordData,
    loadUserDiscordSettings,
    saveDiscordSettings,
    testDiscordWebhook,
    resetToDefaults,
    validateSettings,
    showMessage,

    // Helpers
    getUserId,
    isAdminMode
  }
}
