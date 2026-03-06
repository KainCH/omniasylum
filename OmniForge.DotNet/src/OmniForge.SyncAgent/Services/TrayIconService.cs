using System.Drawing;

namespace OmniForge.SyncAgent.Services
{
    public class TrayIconService : IHostedService, IDisposable
    {
        private readonly StreamingSoftwareMonitor _monitor;
        private readonly ServerConnectionService _serverConnection;
        private readonly PairingService _pairingService;
        private readonly AutoStartService _autoStartService;
        private readonly AutoUpdateService _autoUpdateService;
        private readonly AgentConfigStore _configStore;
        private readonly ILogger<TrayIconService> _logger;
        private Thread? _trayThread;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private System.Windows.Forms.ApplicationContext? _appContext;
        private bool _softwareConnected;
        private bool _serverConnected;
        private bool _softwareDetected;
        private string _currentScene = "";
        private int _checklistCompleted;
        private int _checklistTotal;
        private bool _isPairing;
        private bool _initialSyncShown;

        public TrayIconService(
            StreamingSoftwareMonitor monitor,
            ServerConnectionService serverConnection,
            PairingService pairingService,
            AutoStartService autoStartService,
            AutoUpdateService autoUpdateService,
            AgentConfigStore configStore,
            ILogger<TrayIconService> logger)
        {
            _monitor = monitor;
            _serverConnection = serverConnection;
            _pairingService = pairingService;
            _autoStartService = autoStartService;
            _autoUpdateService = autoUpdateService;
            _configStore = configStore;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _monitor.SoftwareDetected += OnSoftwareDetected;
            _monitor.SoftwareConnected += () => { _softwareConnected = true; UpdateIcon(); OnFirstSync(); };
            _monitor.SoftwareDisconnected += _ => { _softwareConnected = false; UpdateIcon(); };
            _monitor.SceneActivated += scene => { _currentScene = scene; UpdateIcon(); };

            _serverConnection.ServerConnected += () => { _serverConnected = true; UpdateIcon(); RebuildMenu(); OnFirstSync(); };
            _serverConnection.ServerDisconnected += _ => { _serverConnected = false; UpdateIcon(); };
            _serverConnection.ChecklistProgressReceived += (completed, total) =>
            {
                _checklistCompleted = completed;
                _checklistTotal = total;
                UpdateIcon();
            };

            _autoUpdateService.UpdateAvailable += (current, remote) =>
            {
                _notifyIcon?.ShowBalloonTip(8000, "OmniForge Sync Agent — Update Available",
                    $"Version {remote} is available (you have {current}).\nThe update will be applied automatically when your stream ends.",
                    System.Windows.Forms.ToolTipIcon.Info);
            };

            _autoUpdateService.AlreadyUpToDate += current =>
            {
                _notifyIcon?.ShowBalloonTip(3000, "OmniForge Sync Agent",
                    $"You're up to date (v{current}).",
                    System.Windows.Forms.ToolTipIcon.Info);
            };

            _autoUpdateService.UpdateApplying += () =>
            {
                _notifyIcon?.ShowBalloonTip(4000, "OmniForge Sync Agent — Updating",
                    "Applying update and restarting...",
                    System.Windows.Forms.ToolTipIcon.Info);
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

                // OBS password setting (only when using OBS)
                if (_monitor.Client is ObsWebSocketClient)
                {
                    var obsPasswordItem = new System.Windows.Forms.ToolStripMenuItem("OBS WebSocket Password...");
                    obsPasswordItem.Click += (_, _) => PromptObsPassword();
                    settingsMenu.DropDownItems.Add(obsPasswordItem);
                }

                menu.Items.Add(settingsMenu);

                // Update check
                var checkUpdateItem = new System.Windows.Forms.ToolStripMenuItem("Check for Update");
                checkUpdateItem.Click += (_, _) => _ = _autoUpdateService.CheckNowAsync();
                menu.Items.Add(checkUpdateItem);

                // Setup instructions (visible after first detection)
                if (_softwareDetected)
                {
                    menu.Items.Add("Setup Instructions", null, (_, _) => OpenSetupInstructions());
                }

                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Sign Out", null, (_, _) => OnSignOutClicked());
                menu.Items.Add("Uninstall", null, (_, _) => OnUninstallClicked());
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
                else if (!_softwareDetected)
                {
                    color = Color.Orange;
                    tooltip += " - Scanning for streaming software...";
                }
                else if (_serverConnected && _softwareConnected)
                {
                    color = Color.Green;
                    tooltip += $" - Connected to {_monitor.Client?.SoftwareType?.ToUpperInvariant() ?? "Unknown"}";
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
            var softwareName = _monitor.DetectedSoftwareName ?? "Not detected";
            var clientType = _monitor.Client?.SoftwareType ?? "none";
            var status = !_softwareDetected
                ? "Scanning for streaming software..."
                : _serverConnected && _softwareConnected
                    ? "Fully connected"
                    : _serverConnected ? "Server connected, software disconnected"
                    : _softwareConnected ? "Software connected, server disconnected"
                    : "Disconnected";

            System.Windows.Forms.MessageBox.Show(
                $"Status: {status}\nSoftware: {softwareName} ({clientType})\nScene: {_currentScene}",
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

        private async void OnUninstallClicked()
        {
            var confirm = System.Windows.Forms.MessageBox.Show(
                "This will remove the OmniForge Sync Agent from your computer:\n\n" +
                "  • Sign out and disconnect\n" +
                "  • Remove auto-start\n" +
                "  • Delete all saved config and credentials\n" +
                "  • Delete the installed application files\n\n" +
                "Continue?",
                "Uninstall OmniForge Sync Agent",
                System.Windows.Forms.MessageBoxButtons.YesNo,
                System.Windows.Forms.MessageBoxIcon.Warning,
                System.Windows.Forms.MessageBoxDefaultButton.Button2);

            if (confirm != System.Windows.Forms.DialogResult.Yes) return;

            try
            {
                // 1. Disconnect from server
                _configStore.ClearToken();
                try { await _serverConnection.DisconnectAsync(); } catch { }

                // 2. Remove auto-start registry entry
                _autoStartService.Disable();

                // 3. Delete all AppData (config, logs, cached credentials)
                _configStore.DeleteAllData();

                // 4. Schedule self-deletion of the exe via a cmd detach trick:
                //    a short-delay batch command overwrites then deletes the file
                //    after this process exits.
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    var script = $"@echo off\r\n" +
                                 $":loop\r\n" +
                                 $"del /f /q \"{exePath}\" >nul 2>&1\r\n" +
                                 $"if exist \"{exePath}\" (timeout /t 1 /nobreak >nul & goto loop)\r\n" +
                                 $"rd /s /q \"{Path.GetDirectoryName(exePath)}\" >nul 2>&1\r\n";

                    var batPath = Path.Combine(Path.GetTempPath(), "omni-forge-uninstall.bat");
                    File.WriteAllText(batPath, script);

                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
                    {
                        Arguments = $"/c start /min \"\" \"{batPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during uninstall");
            }

            // Exit immediately so the bat can acquire the exe lock
            _notifyIcon?.Dispose();
            _appContext?.ExitThread();
            Environment.Exit(0);
        }

        private void OnSoftwareDetected(string softwareName)
        {
            _softwareDetected = true;
            UpdateIcon();
            RebuildMenu();

            // Toast: announce which software was found
            _notifyIcon?.ShowBalloonTip(5000, "OmniForge Sync Agent",
                $"{softwareName} detected! Scene sync is ready to connect.",
                System.Windows.Forms.ToolTipIcon.Info);

            // If OBS was detected, show a follow-up toast about password requirement
            if (_monitor.Client is ObsWebSocketClient obsClient)
            {
                obsClient.AuthenticationFailed += OnObsAuthFailed;

                // If no saved password, proactively notify that it may be needed
                if (string.IsNullOrEmpty(_configStore.Config.ObsPassword))
                {
                    _notifyIcon?.ShowBalloonTip(7000, "OBS WebSocket Password",
                        "If OBS has WebSocket authentication enabled, set your password in the tray menu:\nRight-click > Settings > OBS WebSocket Password",
                        System.Windows.Forms.ToolTipIcon.Warning);
                }
            }
        }

        private void OnFirstSync()
        {
            // Show setup instructions once when both software and server are connected for the first time
            if (_initialSyncShown || !_softwareConnected || !_serverConnected) return;
            _initialSyncShown = true;

            var softwareName = _monitor.DetectedSoftwareName ?? "streaming software";
            _notifyIcon?.ShowBalloonTip(5000, "OmniForge Sync Agent",
                $"Connected to {softwareName} and OmniForge server!\nOpening setup instructions...",
                System.Windows.Forms.ToolTipIcon.Info);

            // Automatically open the setup instructions page
            OpenSetupInstructions();
        }

        private void OpenSetupInstructions()
        {
            var serverUrl = _configStore.ServerUrl;
            var softwareType = _monitor.Client?.SoftwareType ?? "obs";
            var url = $"{serverUrl.TrimEnd('/')}/sync-setup?software={softwareType}";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open browser for setup instructions");
            }
        }

        private void OnObsAuthFailed()
        {
            _notifyIcon?.ShowBalloonTip(5000, "OmniForge Sync Agent",
                "OBS WebSocket authentication failed. Please set your password in Settings > OBS WebSocket Password.",
                System.Windows.Forms.ToolTipIcon.Warning);

            PromptObsPassword();
        }

        private void PromptObsPassword()
        {
            // Must run on an STA thread for WinForms dialogs
            if (_notifyIcon?.ContextMenuStrip?.InvokeRequired == true)
            {
                _notifyIcon.ContextMenuStrip.Invoke(new Action(PromptObsPassword));
                return;
            }

            var currentPassword = _configStore.Config.ObsPassword ?? "";
            var result = ShowPasswordDialog("OBS WebSocket Password",
                "Enter the password from OBS > Tools > WebSocket Server Settings:",
                currentPassword);

            if (result == null) return; // Cancelled

            _configStore.SaveObsPassword(string.IsNullOrEmpty(result) ? null : result);

            if (_monitor.Client is ObsWebSocketClient obsClient)
            {
                obsClient.SetPassword(string.IsNullOrEmpty(result) ? null : result);

                // Reconnect with the new password
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await obsClient.DisconnectAsync();
                        await obsClient.ConnectAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to reconnect OBS after password change");
                    }
                });
            }

            _notifyIcon?.ShowBalloonTip(3000, "OmniForge Sync Agent",
                "OBS password updated. Reconnecting...",
                System.Windows.Forms.ToolTipIcon.Info);
        }

        private static string? ShowPasswordDialog(string title, string prompt, string currentValue)
        {
            var form = new System.Windows.Forms.Form
            {
                Text = title,
                Width = 420,
                Height = 180,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true
            };

            var label = new System.Windows.Forms.Label { Left = 15, Top = 15, Width = 370, Text = prompt };
            var textBox = new System.Windows.Forms.TextBox
            {
                Left = 15, Top = 45, Width = 370,
                UseSystemPasswordChar = true,
                Text = currentValue
            };
            var okButton = new System.Windows.Forms.Button
            {
                Text = "OK", Left = 220, Top = 85, Width = 80,
                DialogResult = System.Windows.Forms.DialogResult.OK
            };
            var cancelButton = new System.Windows.Forms.Button
            {
                Text = "Cancel", Left = 305, Top = 85, Width = 80,
                DialogResult = System.Windows.Forms.DialogResult.Cancel
            };

            form.Controls.AddRange(new System.Windows.Forms.Control[] { label, textBox, okButton, cancelButton });
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;

            return form.ShowDialog() == System.Windows.Forms.DialogResult.OK ? textBox.Text : null;
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
