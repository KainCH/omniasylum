using System;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace OmniForge.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/logs")]
    public class LogsController : ControllerBase
    {
        private readonly ILogger<LogsController> _logger;
        private readonly IWebHostEnvironment _environment;

        public LogsController(ILogger<LogsController> logger, IWebHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var userId = User.FindFirst("userId")?.Value;
            _logger.LogInformation("📊 Log status requested by user {UserId}", userId);

            var process = Process.GetCurrentProcess();

            var status = new
            {
                status = "active",
                logLevel = "INFO", // In .NET this is dynamic, but we can hardcode or retrieve from config
                categories = new[] { "MAIN", "API", "AUTH", "TWITCH", "DATABASE" },
                output = "console (Azure Log Analytics)",
                uptime = (DateTime.Now - process.StartTime).TotalSeconds,
                memoryUsage = new
                {
                    rss = process.WorkingSet64,
                    heapTotal = GC.GetTotalMemory(false),
                    heapUsed = GC.GetTotalMemory(false), // Approximation
                    external = 0
                },
                environment = _environment.EnvironmentName
            };

            return Ok(new
            {
                success = true,
                logging = status,
                message = "Console logging active - check Azure Log Analytics workspace"
            });
        }

        [HttpPost("test")]
        public IActionResult TestLog()
        {
            var userId = User.FindFirst("userId")?.Value;
            var testId = Guid.NewGuid().ToString().Substring(0, 8);

            _logger.LogInformation("🧪 TEST LOG: Info level test {TestId} User: {UserId}", testId, userId);
            _logger.LogWarning("🧪 TEST LOG: Warning level test {TestId} User: {UserId}", testId, userId);
            _logger.LogError("🧪 TEST LOG: Error level test {TestId} User: {UserId}", testId, userId);

            return Ok(new
            {
                success = true,
                message = "Test logs generated",
                testId,
                timestamp = DateTimeOffset.UtcNow
            });
        }
    }
}
