/**
 * Notification Helper Functions and Factories
 * Provides centralized logic for managing notification settings
 */

// Default notification settings factory
export const createDefaultNotificationSettings = () => ({
  templateStyle: 'custom', // Disabled template system for now: asylum_themed, minimal, detailed, custom
  discordNotifications: {
    death_milestone: false, // Disabled by default until template system is working
    swear_milestone: false, // Disabled by default until template system is working
    stream_start: false,
    stream_end: false,
    follower_goal: false,
    subscriber_milestone: false,
    channel_point_redemption: false
  },
  channelNotifications: {
    death_milestone: false, // Disabled by default until template system is working
    swear_milestone: false, // Disabled by default until template system is working
    stream_start: false,
    stream_end: false,
    follower_goal: false,
    subscriber_milestone: false,
    channel_point_redemption: false
  },
  milestoneThresholds: {
    deaths: [10, 25, 50, 100, 250, 500],
    swears: [25, 50, 100, 200, 500]
  }
});

// Notification type definitions factory
export const createNotificationTypes = () => [
  {
    id: 'death_milestone',
    icon: '💀',
    title: 'Death Milestones',
    description: 'Notify when reaching death count goals (currently disabled)',
    supportsDiscord: true,
    supportsChannel: true,
    defaultDiscord: false, // Disabled until template system is working
    defaultChannel: false  // Disabled until template system is working
  },
  {
    id: 'swear_milestone',
    icon: '🤬',
    title: 'Swear Milestones',
    description: 'Track profanity milestones (currently disabled)',
    supportsDiscord: true,
    supportsChannel: true,
    defaultDiscord: false, // Disabled until template system is working
    defaultChannel: false  // Disabled until template system is working
  },
  {
    id: 'stream_start',
    icon: '🔴',
    title: 'Stream Start',
    description: 'Announce when you go live',
    supportsDiscord: true,
    supportsChannel: false,
    defaultDiscord: true,
    defaultChannel: false
  },
  {
    id: 'stream_end',
    icon: '⚫',
    title: 'Stream End',
    description: 'Announce when stream ends',
    supportsDiscord: true,
    supportsChannel: false,
    defaultDiscord: true,
    defaultChannel: false
  },
  {
    id: 'follower_goal',
    icon: '👥',
    title: 'Follower Goals',
    description: 'Celebrate follower milestones',
    supportsDiscord: true,
    supportsChannel: true,
    defaultDiscord: false,
    defaultChannel: false
  },
  {
    id: 'subscriber_milestone',
    icon: '⭐',
    title: 'Subscriber Milestones',
    description: 'Track subscription goals',
    supportsDiscord: true,
    supportsChannel: true,
    defaultDiscord: false,
    defaultChannel: false
  },
  {
    id: 'channel_point_redemption',
    icon: '🎁',
    title: 'Channel Points',
    description: 'Major channel point redemptions',
    supportsDiscord: true,
    supportsChannel: false,
    defaultDiscord: false,
    defaultChannel: false
  }
];

// Template style definitions factory
export const createTemplateStyles = () => [
  {
    id: 'asylum_themed',
    name: 'Asylum Themed',
    icon: '🏚️',
    description: 'Dark and atmospheric notifications',
    previewColor: 'red',
    previewText: 'The asylum awaits...'
  },
  {
    id: 'minimal',
    name: 'Minimal',
    icon: '⚪',
    description: 'Clean and simple notifications',
    previewColor: 'blue',
    previewText: 'Simple & clean'
  },
  {
    id: 'detailed',
    name: 'Detailed',
    icon: '📊',
    description: 'Rich notifications with stats',
    previewColor: 'green',
    previewText: 'Progress & achievements'
  }
];

// Helper function to update notification setting
export const updateNotificationSetting = (settings, type, platform, enabled) => {
  const platformKey = platform === 'discord' ? 'discordNotifications' : 'channelNotifications';
  return {
    ...settings,
    [platformKey]: {
      ...settings[platformKey],
      [type]: enabled
    }
  };
};

// Helper function to update template style
export const updateTemplateStyle = (settings, newStyle) => ({
  ...settings,
  templateStyle: newStyle
});

// Helper function to update milestone thresholds
export const updateMilestoneThresholds = (settings, counterType, thresholds) => ({
  ...settings,
  milestoneThresholds: {
    ...settings.milestoneThresholds,
    [counterType]: thresholds
  }
});

// Helper function to validate notification settings
export const validateNotificationSettings = (settings) => {
  const errors = [];

  if (!settings?.templateStyle) {
    errors.push('Template style is required');
  }

  if (!settings?.milestoneThresholds?.deaths || !Array.isArray(settings.milestoneThresholds.deaths)) {
    errors.push('Death milestone thresholds must be an array');
  }

  if (!settings?.milestoneThresholds?.swears || !Array.isArray(settings.milestoneThresholds.swears)) {
    errors.push('Swear milestone thresholds must be an array');
  }

  return {
    isValid: errors.length === 0,
    errors
  };
};

// Helper function to parse threshold string input
export const parseThresholdString = (input) => {
  return input
    .split(',')
    .map(v => parseInt(v.trim()))
    .filter(v => !isNaN(v) && v > 0)
    .sort((a, b) => a - b);
};

// Helper function to format thresholds for display
export const formatThresholdsForDisplay = (thresholds) => {
  if (!Array.isArray(thresholds)) return '';
  return thresholds.join(', ');
};

// Helper function to check if notification type is enabled for any platform
export const isNotificationTypeEnabled = (settings, notificationType) => {
  return settings?.discordNotifications?.[notificationType] ||
         settings?.channelNotifications?.[notificationType];
};

// Helper function to get enabled platforms for notification type
export const getEnabledPlatforms = (settings, notificationType) => {
  const platforms = [];
  if (settings?.discordNotifications?.[notificationType]) platforms.push('discord');
  if (settings?.channelNotifications?.[notificationType]) platforms.push('channel');
  return platforms;
};

// Factory for creating notification settings update handlers
export const createNotificationHandlers = (setSettings) => ({
  updateDiscordNotification: (type, enabled) => {
    setSettings(current => updateNotificationSetting(current, type, 'discord', enabled));
  },

  updateChannelNotification: (type, enabled) => {
    setSettings(current => updateNotificationSetting(current, type, 'channel', enabled));
  },

  updateTemplate: (style) => {
    setSettings(current => updateTemplateStyle(current, style));
  },

  updateThresholds: (counterType, thresholdString) => {
    const thresholds = parseThresholdString(thresholdString);
    setSettings(current => updateMilestoneThresholds(current, counterType, thresholds));
  },

  resetToDefaults: () => {
    setSettings(createDefaultNotificationSettings());
  }
});
