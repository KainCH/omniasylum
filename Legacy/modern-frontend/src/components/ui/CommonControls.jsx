import React from 'react';

/**
 * Reusable Toggle Switch Component
 */
export const ToggleSwitch = ({
  checked,
  onChange,
  label,
  disabled = false,
  size = 'medium', // small, medium, large
  labelPosition = 'right' // left, right
}) => {
  const sizeClasses = {
    small: 'toggle-small',
    medium: 'toggle-medium',
    large: 'toggle-large'
  };

  const labelElement = label && (
    <span className="toggle-label">{label}</span>
  );

  return (
    <label className={`toggle-switch ${sizeClasses[size]} ${disabled ? 'disabled' : ''} ${labelPosition === 'left' ? 'label-left' : 'label-right'}`}>
      {labelPosition === 'left' && labelElement}
      <input
        type="checkbox"
        checked={checked}
        onChange={onChange}
        disabled={disabled}
      />
      <span className="toggle-slider"></span>
      {labelPosition === 'right' && labelElement}
    </label>
  );
};

/**
 * Notification Type Card Component
 */
export const NotificationTypeCard = ({
  notificationType,
  discordEnabled,
  channelEnabled,
  onDiscordChange,
  onChannelChange,
  disabled = false
}) => {
  return (
    <div className={`notification-type-card ${disabled ? 'disabled' : ''}`}>
      <div className="notification-header">
        <div className="notification-icon">{notificationType?.icon || '🔔'}</div>
        <div className="notification-info">
          <h5>{notificationType?.title || 'Unknown Notification'}</h5>
          <p>{notificationType?.description || 'Description unavailable'}</p>
        </div>
      </div>

      <div className="notification-controls">
        {notificationType?.supportsDiscord && (
          <ToggleSwitch
            checked={discordEnabled}
            onChange={onDiscordChange}
            label="Discord"
            disabled={disabled}
            size="small"
          />
        )}

        {notificationType?.supportsChannel && (
          <ToggleSwitch
            checked={channelEnabled}
            onChange={onChannelChange}
            label="Chat"
            disabled={disabled}
            size="small"
          />
        )}
      </div>
    </div>
  );
};

/**
 * Template Style Selection Component
 */
export const TemplateStyleSelector = ({
  templates,
  selectedStyle,
  onStyleChange,
  disabled = false
}) => {
  return (
    <div className="template-style-selector">
      <div className="template-grid">
        {templates.map(template => (
          <div
            key={template?.id}
            className={`template-option ${selectedStyle === template?.id ? 'selected' : ''} ${disabled ? 'disabled' : ''}`}
            onClick={() => !disabled && onStyleChange(template?.id)}
          >
            <div className="template-icon">{template?.icon || '🎨'}</div>
            <h5>{template?.name || 'Unknown Template'}</h5>
            <p>{template?.description || 'Template description unavailable'}</p>
            <div className={`template-preview ${template?.previewColor}`}>
              <span>{template?.previewText}</span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};

/**
 * Milestone Threshold Input Component
 */
export const MilestoneThresholdInput = ({
  label,
  icon,
  value,
  onChange,
  placeholder = "10, 25, 50, 100",
  disabled = false
}) => {
  return (
    <div className={`milestone-input ${disabled ? 'disabled' : ''}`}>
      <label>
        <div className="milestone-label">
          <span className="milestone-icon">{icon}</span>
          <strong>{label}</strong>
        </div>
        <input
          type="text"
          value={value}
          onChange={onChange}
          placeholder={placeholder}
          disabled={disabled}
          className="threshold-input"
        />
        <small>Comma-separated numbers (e.g., 10, 25, 50)</small>
      </label>
    </div>
  );
};

/**
 * Action Button Component
 */
export const ActionButton = ({
  onClick,
  disabled = false,
  loading = false,
  variant = 'primary', // primary, secondary, danger, success
  size = 'medium', // small, medium, large
  icon,
  children,
  className = ''
}) => {
  const variantClasses = {
    primary: 'btn-primary',
    secondary: 'btn-secondary',
    danger: 'btn-danger',
    success: 'btn-success'
  };

  const sizeClasses = {
    small: 'btn-small',
    medium: 'btn-medium',
    large: 'btn-large'
  };

  return (
    <button
      onClick={onClick}
      disabled={disabled || loading}
      className={`btn ${variantClasses[variant]} ${sizeClasses[size]} ${loading ? 'loading' : ''} ${className}`}
    >
      {loading ? (
        <span className="loading-spinner">⏳</span>
      ) : icon ? (
        <span className="btn-icon">{icon}</span>
      ) : null}
      {children}
    </button>
  );
};

/**
 * Form Section Component
 */
export const FormSection = ({
  title,
  description,
  icon,
  children,
  collapsible = false,
  defaultExpanded = true,
  className = ''
}) => {
  const [expanded, setExpanded] = React.useState(defaultExpanded);

  return (
    <div className={`form-section ${className} ${collapsible && !expanded ? 'collapsed' : ''}`}>
      <div
        className={`section-header ${collapsible ? 'clickable' : ''}`}
        onClick={() => collapsible && setExpanded(!expanded)}
      >
        {icon && <span className="section-icon">{icon}</span>}
        <div className="section-title">
          <h4>{title}</h4>
          {description && <p>{description}</p>}
        </div>
        {collapsible && (
          <span className="collapse-indicator">
            {expanded ? '▼' : '▶'}
          </span>
        )}
      </div>

      {expanded && (
        <div className="section-content">
          {children}
        </div>
      )}
    </div>
  );
};

/**
 * Status Badge Component
 */
export const StatusBadge = ({
  status, // success, warning, error, info
  children,
  size = 'medium',
  icon
}) => {
  const statusClasses = {
    success: 'status-success',
    warning: 'status-warning',
    error: 'status-error',
    info: 'status-info'
  };

  const sizeClasses = {
    small: 'badge-small',
    medium: 'badge-medium',
    large: 'badge-large'
  };

  return (
    <span className={`status-badge ${statusClasses[status]} ${sizeClasses[size]}`}>
      {icon && <span className="badge-icon">{icon}</span>}
      {children}
    </span>
  );
};

/**
 * Input Group Component
 */
export const InputGroup = ({
  label,
  error,
  required = false,
  children,
  className = ''
}) => {
  return (
    <div className={`input-group ${error ? 'error' : ''} ${className}`}>
      {label && (
        <label className="input-label">
          {label}
          {required && <span className="required">*</span>}
        </label>
      )}
      <div className="input-wrapper">
        {children}
      </div>
      {error && (
        <span className="error-message">{error}</span>
      )}
    </div>
  );
};
