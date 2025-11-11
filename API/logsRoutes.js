const express = require('express');
const { requireAuth, requireAdmin } = require('./authMiddleware');

const router = express.Router();

/**
 * Get recent logs instructions
 * GET /api/logs/info
 */
router.get('/info', requireAuth, async (req, res) => {
  res.json({
    message: 'Logs are available in Azure Log Analytics',
    instructions: {
      logAnalytics: 'View logs in Azure Portal > Log Analytics workspace',
      kusto: 'Use KQL queries to filter and analyze logs',
      exampleQuery: `ContainerAppConsoleLogs_CL
| where ContainerAppName_s == "omniforgestream-api-prod"
| where TimeGenerated > ago(1h)
| project TimeGenerated, Log_s
| order by TimeGenerated desc
| limit 100`
    },
    azurePortalUrl: 'https://portal.azure.com',
    note: 'All console.log output is automatically captured by Azure Log Analytics'
  });
});

/**
 * Application insights for logging
 * GET /api/logs/insights
 */
router.get('/insights', requireAuth, async (req, res) => {
  res.json({
    service: 'OmniAsylum-API',
    logFormat: 'JSON structured logging',
    categories: [
      'API_REQUEST - HTTP request logs',
      'API_RESPONSE - HTTP response logs',
      'DATABASE - Database operation logs',
      'AUTH - Authentication attempts',
      'TWITCH - Twitch service operations',
      'WEBSOCKET - Socket.io events',
      'ERROR - Application errors'
    ],
    queryTips: [
      'Filter by level: | where Log_s contains "ERROR"',
      'Filter by category: | where Log_s contains "API_REQUEST"',
      'Filter by user: | where Log_s contains "userId"',
      'Time range: | where TimeGenerated > ago(30m)'
    ]
  });
});

module.exports = router;
