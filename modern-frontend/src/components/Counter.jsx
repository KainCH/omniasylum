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
          <button
            className="counter-btn decrement"
            onClick={onDecrementDeaths}
            title="Decrease deaths"
          >
            -
          </button>
          <button
            className="counter-btn increment"
            onClick={onIncrementDeaths}
            title="Increase deaths"
          >
            +
          </button>
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
          <button
            className="counter-btn decrement"
            onClick={onDecrementSwears}
            title="Decrease swears"
          >
            -
          </button>
          <button
            className="counter-btn increment"
            onClick={onIncrementSwears}
            title="Increase swears"
          >
            +
          </button>
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
        <button
          className="action-btn reset-btn"
          onClick={onReset}
          title="Reset all counters to zero"
        >
          🔄 Reset All
        </button>
        <button
          className="action-btn export-btn"
          onClick={onExport}
          title="Export counter data as JSON"
        >
          💾 Export Data
        </button>
      </div>
    </div>
  )
}

export default Counter
