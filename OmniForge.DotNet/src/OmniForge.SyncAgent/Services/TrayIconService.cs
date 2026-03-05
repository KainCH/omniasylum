using System.Drawing;

namespace OmniForge.SyncAgent.Services
{
    public class TrayIconService : IHostedService, IDisposable
    {
        private readonly StreamingSoftwareMonitor _monitor;
        private readonly ServerConnectionService _serverConnection;
        private readonly IConfiguration _config;
        private readonly ILogger<TrayIconService> _logger;
        private Thread? _trayThread;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private System.Windows.Forms.ApplicationContext? _appContext;
        private bool _softwareConnected;
        private bool _serverConnected;
        private string _currentScene = "";
        private int _checklistCompleted;
        private int _checklistTotal;

        public TrayIconService(
            StreamingSoftwareMonitor monitor,
            ServerConnectionService serverConnection,
            IConfiguration config,
            ILogger<TrayIconService> logger)
        {
            _monitor = monitor;
            _serverConnection = serverConnection;
            _config = config;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _monitor.SoftwareConnected += () => { _softwareConnected = true; UpdateIcon(); };
            _monitor.SoftwareDisconnected += _ => { _softwareConnected = false; UpdateIcon(); };
            _monitor.SceneActivated += scene => { _currentScene = scene; UpdateIcon(); };

            _serverConnection.ServerConnected += () => { _serverConnected = true; UpdateIcon(); };
            _serverConnection.ServerDisconnected += _ => { _serverConnected = false; UpdateIcon(); };
            _serverConnection.ChecklistProgressReceived += (completed, total) =>
            {
                _checklistCompleted = completed;
                _checklistTotal = total;
                UpdateIcon();
            };

            _trayThread = new Thread(RunTray);
            _trayThread.SetApartmentState(ApartmentState.STA);
            _trayThread.IsBackground = true;
            _trayThread.Start();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            _appContext?.ExitThread();
            return Task.CompletedTask;
        }

        private void RunTray()
        {
            try
            {
                _notifyIcon = new System.Windows.Forms.NotifyIcon
                {
                    Text = "OmniForge Sync Agent",
                    Visible = true
                };

                UpdateIcon();

                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("Status", null, (_, _) => ShowStatus());
                menu.Items.Add("Open Pre-Flight", null, (_, _) => OpenPreFlight());
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Exit", null, (_, _) => ExitApp());

                _notifyIcon.ContextMenuStrip = menu;

                _appContext = new System.Windows.Forms.ApplicationContext();
                System.Windows.Forms.Application.Run(_appContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tray icon thread error");
            }
        }

        private void UpdateIcon()
        {
            if (_notifyIcon == null) return;

            try
            {
                var tooltip = "OmniForge Sync Agent";
                Color color;

                if (_serverConnected && _softwareConnected)
                {
                    color = Color.Green;
                    tooltip += $" - Connected to {_monitor.Client.SoftwareType.ToUpperInvariant()}";
                    if (!string.IsNullOrEmpty(_currentScene))
                        tooltip += $"\nScene: {_currentScene}";
                }
                else if (_serverConnected || _softwareConnected)
                {
                    color = Color.Yellow;
                    tooltip += _serverConnected ? " - Server OK, software disconnected" : " - Software OK, server disconnected";
                }
                else
                {
                    color = Color.Red;
                    tooltip += " - Disconnected";
                }

                if (_checklistTotal > 0)
                {
                    tooltip += $"\nPre-Flight: {_checklistCompleted}/{_checklistTotal} complete";
                }

                // Create a simple colored circle icon
                var bitmap = new Bitmap(16, 16);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    using var brush = new SolidBrush(color);
                    g.FillEllipse(brush, 1, 1, 14, 14);
                }

                var oldIcon = _notifyIcon.Icon;
                _notifyIcon.Icon = Icon.FromHandle(bitmap.GetHicon());
                _notifyIcon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
                oldIcon?.Dispose();
                bitmap.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update tray icon");
            }
        }

        private void ShowStatus()
        {
            var status = _serverConnected && _softwareConnected
                ? "Fully connected"
                : _serverConnected ? "Server connected, software disconnected"
                : _softwareConnected ? "Software connected, server disconnected"
                : "Disconnected";

            System.Windows.Forms.MessageBox.Show(
                $"Status: {status}\nSoftware: {_monitor.Client.SoftwareType}\nScene: {_currentScene}",
                "OmniForge Sync Agent",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Information);
        }

        private void OpenPreFlight()
        {
            var serverUrl = _config.GetValue<string>("OmniForge:ServerUrl") ?? "https://localhost:5001";
            var url = $"{serverUrl.TrimEnd('/')}/preflight";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open browser for pre-flight");
            }
        }

        private void ExitApp()
        {
            _notifyIcon?.Dispose();
            _appContext?.ExitThread();
            Environment.Exit(0);
        }

        public void Dispose()
        {
            _notifyIcon?.Dispose();
        }
    }
}
