import React, { useState, useEffect } from 'react';
import {
  createDefaultNotificationSettings,
  createNotificationTypes,
  createTemplateStyles,
  createNotificationHandlers,
  validateNotificationSettings,
  formatThresholdsForDisplay,
  parseThresholdString
} from '../utils/notificationHelpers';
import {
  NotificationTypeCard,
  TemplateStyleSelector,
  MilestoneThresholdInput,
  ActionButton,
  FormSection,
  StatusBadge,
  InputGroup
} from './ui/CommonControls';
import '../styles/CommonControls.css';

const NotificationSettings = ({
  user,
  initialSettings,
  onSave,
  onTest,
  saving = false,
  testing = false
}) => {
  const [settings, setSettings] = useState(createDefaultNotificationSettings());
  const [webhookUrl, setWebhookUrl] = useState('');
  const [validation, setValidation] = useState({ isValid: true, errors: [] });

  // Initialize settings when props change
  useEffect(() => {
    if (initialSettings) {
      setSettings(initialSettings);
    }
    if (user?.discordWebhookUrl) {
      setWebhookUrl(user.discordWebhookUrl);
    }
  }, [initialSettings, user]);

  // Validate settings when they change
  useEffect(() => {
    setValidation(validateNotificationSettings(settings));
  }, [settings]);

  // Create handlers using our factory
  const handlers = createNotificationHandlers(setSettings);

  // Get data from our factories
  const notificationTypes = createNotificationTypes();
  const templateStyles = createTemplateStyles();

  // Handle save
  const handleSave = async () => {
    if (!validation.isValid) {
      return;
    }

    try {
      await onSave({
        discordWebhookUrl: webhookUrl,
        discordSettings: settings
      });
    } catch (error) {
      console.error('Failed to save notification settings:', error);
    }
  };

  // Handle test
  const handleTest = async () => {
    if (!webhookUrl) {
      return;
    }

    try {
      await onTest(webhookUrl);
    } catch (error) {
      console.error('Failed to test webhook:', error);
    }
  };

  // Handle threshold updates
  const handleThresholdChange = (counterType, value) => {
    const thresholds = parseThresholdString(value);
    handlers.updateThresholds(counterType, value);
  };

  return (
    <div className="notification-settings">
      <div className="settings-header">
        <h3>🔔 Discord Notifications</h3>
        <p>
          Configure Discord webhook and notification preferences for {user?.displayName}'s stream events.
        </p>

        {!validation.isValid && (
          <div className="validation-errors">
            {validation.errors.map((error, index) => (
              <StatusBadge key={index} status="error" icon="⚠️">
                {error}
              </StatusBadge>
            ))}
          </div>
        )}
      </div>

      {/* Webhook Configuration */}
      <FormSection
        title="🔗 Webhook Configuration"
        description="Set up your Discord webhook URL to receive notifications"
        icon="🔗"
      >
        <InputGroup
          label="Discord Webhook URL"
          required
          error={!webhookUrl ? 'Webhook URL is required' : ''}
        >
          <input
            type="url"
            value={webhookUrl}
            onChange={(e) => setWebhookUrl(e.target.value)}
            placeholder="https://discord.com/api/webhooks/..."
            className="webhook-input"
          />
        </InputGroup>

        <div className="webhook-actions">
          <ActionButton
            onClick={handleTest}
            disabled={!webhookUrl || testing}
            loading={testing}
            variant="secondary"
            icon="🧪"
          >
            {testing ? 'Testing...' : 'Send Test'}
          </ActionButton>
        </div>
      </FormSection>

      {/* Template Style Selection */}
      <FormSection
        title="🎨 Notification Style"
        description="Choose how your Discord notifications look"
        icon="🎨"
      >
        <TemplateStyleSelector
          templates={templateStyles}
          selectedStyle={settings.templateStyle}
          onStyleChange={handlers.updateTemplate}
          disabled={saving}
        />
      </FormSection>

      {/* Notification Types */}
      <FormSection
        title="🔔 Notification Types"
        description="Choose which events to announce and where"
        icon="🔔"
      >
        <div className="notification-types-grid">
          {notificationTypes.map(type => (
            <NotificationTypeCard
              key={type.id}
              notificationType={type}
              discordEnabled={settings.discordNotifications[type.id]}
              channelEnabled={settings.channelNotifications[type.id]}
              onDiscordChange={(e) => handlers.updateDiscordNotification(type.id, e.target.checked)}
              onChannelChange={(e) => handlers.updateChannelNotification(type.id, e.target.checked)}
              disabled={saving}
            />
          ))}
        </div>
      </FormSection>

      {/* Milestone Thresholds */}
      <FormSection
        title="🎯 Milestone Thresholds"
        description="Configure when milestone notifications are triggered"
        icon="🎯"
      >
        <div className="milestone-thresholds">
          <MilestoneThresholdInput
            label="Death Milestones"
            icon="💀"
            value={formatThresholdsForDisplay(settings.milestoneThresholds.deaths)}
            onChange={(e) => handleThresholdChange('deaths', e.target.value)}
            placeholder="10, 25, 50, 100, 250, 500"
            disabled={saving}
          />

          <MilestoneThresholdInput
            label="Swear Milestones"
            icon="🤬"
            value={formatThresholdsForDisplay(settings.milestoneThresholds.swears)}
            onChange={(e) => handleThresholdChange('swears', e.target.value)}
            placeholder="25, 50, 100, 200, 500"
            disabled={saving}
          />
        </div>
      </FormSection>

      {/* Save Actions */}
      <div className="settings-actions">
        <ActionButton
          onClick={handlers.resetToDefaults}
          disabled={saving}
          variant="secondary"
          icon="🔄"
        >
          Reset to Defaults
        </ActionButton>

        <ActionButton
          onClick={handleSave}
          disabled={!validation.isValid || saving}
          loading={saving}
          variant="primary"
          icon="💾"
        >
          {saving ? 'Saving...' : 'Save Settings'}
        </ActionButton>
      </div>
    </div>
  );
};

export default NotificationSettings;
