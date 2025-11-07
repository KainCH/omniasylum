import { ActionButton } from './ui/CommonControls'

function Counter({
  counters,
  onIncrementDeaths,
  onDecrementDeaths,
  onIncrementSwears,
  onDecrementSwears,
  onReset,
  onExport
}) {
  const total = counters.deaths + counters.swears

  return (
    <div className="counter-grid">
      {/* Deaths Counter */}
      <div className="counter-card deaths">
        <div className="counter-header">
          <h2>💀 Deaths</h2>
        </div>
        <div className="counter-value" id="deaths-count">
          {counters.deaths}
        </div>
        <div className="counter-controls">
          <ActionButton
            variant="danger"
            onClick={onDecrementDeaths}
            title="Decrease deaths"
            size="small"
          >
            -
          </ActionButton>
          <ActionButton
            variant="primary"
            onClick={onIncrementDeaths}
            title="Increase deaths"
            size="small"
          >
            +
          </ActionButton>
        </div>
      </div>

      {/* Swears Counter */}
      <div className="counter-card swears">
        <div className="counter-header">
          <h2>🤬 Swears</h2>
        </div>
        <div className="counter-value" id="swears-count">
          {counters.swears}
        </div>
        <div className="counter-controls">
          <ActionButton
            variant="danger"
            onClick={onDecrementSwears}
            title="Decrease swears"
            size="small"
          >
            -
          </ActionButton>
          <ActionButton
            variant="primary"
            onClick={onIncrementSwears}
            title="Increase swears"
            size="small"
          >
            +
          </ActionButton>
        </div>
      </div>

      {/* Total Counter */}
      <div className="counter-card total">
        <div className="counter-header">
          <h2>📊 Total</h2>
        </div>
        <div className="counter-value" id="total-count">
          {total}
        </div>
        <div className="counter-info">
          <p>Combined count</p>
        </div>
      </div>

      {/* Action Buttons */}
      <div className="action-buttons">
        <ActionButton
          variant="warning"
          onClick={onReset}
          title="Reset all counters to zero"
        >
          🔄 Reset All
        </ActionButton>
        <ActionButton
          variant="secondary"
          onClick={onExport}
          title="Export counter data as JSON"
        >
          💾 Export Data
        </ActionButton>
      </div>
    </div>
  )
}

export default Counter
