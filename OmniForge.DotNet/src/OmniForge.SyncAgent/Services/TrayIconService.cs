using System.Drawing;

namespace OmniForge.SyncAgent.Services
{
    public class TrayIconService : IHostedService, IDisposable
    {
        private readonly StreamingSoftwareMonitor _monitor;
        private readonly ServerConnectionService _serverConnection;
        private readonly PairingService _pairingService;
        private readonly AutoStartService _autoStartService;
        private readonly AgentConfigStore _configStore;
        private readonly ILogger<TrayIconService> _logger;
        private Thread? _trayThread;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private System.Windows.Forms.ApplicationContext? _appContext;
        private bool _softwareConnected;
        private bool _serverConnected;
        private string _currentScene = "";
        private int _checklistCompleted;
        private int _checklistTotal;
        private bool _isPairing;

        public TrayIconService(
            StreamingSoftwareMonitor monitor,
            ServerConnectionService serverConnection,
            PairingService pairingService,
            AutoStartService autoStartService,
            AgentConfigStore configStore,
            ILogger<TrayIconService> logger)
        {
            _monitor = monitor;
            _serverConnection = serverConnection;
            _pairingService = pairingService;
            _autoStartService = autoStartService;
            _configStore = configStore;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _monitor.SoftwareConnected += () => { _softwareConnected = true; UpdateIcon(); };
            _monitor.SoftwareDisconnected += _ => { _softwareConnected = false; UpdateIcon(); };
            _monitor.SceneActivated += scene => { _currentScene = scene; UpdateIcon(); };

            _serverConnection.ServerConnected += () => { _serverConnected = true; UpdateIcon(); RebuildMenu(); };
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
                RebuildMenu();

                _appContext = new System.Windows.Forms.ApplicationContext();
                System.Windows.Forms.Application.Run(_appContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tray icon thread error");
            }
        }

        private void RebuildMenu()
        {
            if (_notifyIcon == null) return;

            try
            {
                if (_notifyIcon.InvokeRequired())
                {
                    // Marshal to the tray thread
                    _notifyIcon.ContextMenuStrip?.Invoke(new Action(RebuildMenu));
                    return;
                }
            }
            catch
            {
                // If invoke check fails, just build the menu directly
            }

            var menu = new System.Windows.Forms.ContextMenuStrip();

            if (_configStore.HasToken())
            {
                menu.Items.Add("Status", null, (_, _) => ShowStatus());
                menu.Items.Add("Open Pre-Flight", null, (_, _) => OpenPreFlight());
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

                // Settings submenu
                var settingsMenu = new System.Windows.Forms.ToolStripMenuItem("Settings");
                var autoStartItem = new System.Windows.Forms.ToolStripMenuItem("Start with Windows")
                {
                    Checked = _autoStartService.IsEnabled(),
                    CheckOnClick = true
                };
                autoStartItem.CheckedChanged += (_, _) =>
                {
                    if (autoStartItem.Checked)
                    {
                        _autoStartService.Enable();
                        _configStore.Config.StartWithWindows = true;
                    }
                    else
                    {
                        _autoStartService.Disable();
                        _configStore.Config.StartWithWindows = false;
                    }
                    _configStore.Save(_configStore.Config);
                };
                settingsMenu.DropDownItems.Add(autoStartItem);
                menu.Items.Add(settingsMenu);

                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Sign Out", null, (_, _) => OnSignOutClicked());
            }
            else
            {
                if (_isPairing)
                {
                    menu.Items.Add("Signing in...", null, null!);
                    menu.Items[0].Enabled = false;
                }
                else
                {
                    menu.Items.Add("Sign In", null, (_, _) => OnSignInClicked());
                }
            }

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitApp());

            _notifyIcon.ContextMenuStrip = menu;
        }

        private void UpdateIcon()
        {
            if (_notifyIcon == null) return;

            try
            {
                var tooltip = "OmniForge Sync Agent";
                Color color;

                if (!_configStore.HasToken())
                {
                    color = Color.Gray;
                    tooltip += " - Not signed in";
                }
                else if (_serverConnected && _softwareConnected)
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

        private async void OnSignInClicked()
        {
            _isPairing = true;
            RebuildMenu();

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var token = await _pairingService.PairAsync(cts.Token);

                if (!string.IsNullOrEmpty(token))
                {
                    await _serverConnection.UpdateTokenAndReconnectAsync(token);

                    // Enable auto-start on first sign-in if preference says so
                    if (_configStore.Config.StartWithWindows && !_autoStartService.IsEnabled())
                    {
                        _autoStartService.Enable();
                    }

                    _notifyIcon?.ShowBalloonTip(3000, "OmniForge Sync Agent",
                        "Successfully signed in and connected!", System.Windows.Forms.ToolTipIcon.Info);
                }
                else
                {
                    _notifyIcon?.ShowBalloonTip(3000, "OmniForge Sync Agent",
                        "Sign in was cancelled or expired.", System.Windows.Forms.ToolTipIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during sign-in");
                _notifyIcon?.ShowBalloonTip(3000, "OmniForge Sync Agent",
                    "Sign in failed. Please try again.", System.Windows.Forms.ToolTipIcon.Error);
            }
            finally
            {
                _isPairing = false;
                UpdateIcon();
                RebuildMenu();
            }
        }

        private async void OnSignOutClicked()
        {
            _configStore.ClearToken();
            _autoStartService.Disable();
            await _serverConnection.DisconnectAsync();
            _serverConnected = false;
            UpdateIcon();
            RebuildMenu();
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
            var serverUrl = _configStore.ServerUrl;
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

    internal static class NotifyIconExtensions
    {
        public static bool InvokeRequired(this System.Windows.Forms.NotifyIcon icon)
        {
            return icon.ContextMenuStrip?.InvokeRequired ?? false;
        }
    }
}
