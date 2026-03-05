using Microsoft.Win32;

namespace OmniForge.SyncAgent.Services
{
    public class AutoStartService
    {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "OmniForge SyncAgent";

        private readonly ILogger<AutoStartService> _logger;

        public AutoStartService(ILogger<AutoStartService> logger)
        {
            _logger = logger;
        }

        public void Enable()
        {
            try
            {
                var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
                key?.SetValue(ValueName, $"\"{exePath}\"");
                _logger.LogInformation("Auto-start enabled: {Path}", exePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enable auto-start");
            }
        }

        public void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
                _logger.LogInformation("Auto-start disabled");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to disable auto-start");
            }
        }

        public bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                return key?.GetValue(ValueName) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
