using System.Net.Http.Json;
using System.Security.Cryptography;

namespace OmniForge.SyncAgent.Services
{
    public class PairingService
    {
        private readonly AgentConfigStore _configStore;
        private readonly ILogger<PairingService> _logger;

        private static readonly char[] CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

        public PairingService(AgentConfigStore configStore, ILogger<PairingService> logger)
        {
            _configStore = configStore;
            _logger = logger;
        }

        public async Task<string?> PairAsync(CancellationToken ct)
        {
            var code = GenerateCode();
            var serverUrl = _configStore.ServerUrl.TrimEnd('/');

            using var httpClient = new HttpClient();

            // Initiate pairing
            _logger.LogInformation("Initiating pairing with code {Code}", code);
            var initiateResponse = await httpClient.PostAsJsonAsync(
                $"{serverUrl}/auth/pair/initiate",
                new { code },
                ct);

            if (!initiateResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to initiate pairing: {Status}", initiateResponse.StatusCode);
                return null;
            }

            // Open browser
            var pairUrl = $"{serverUrl}/pair?code={code}";
            _logger.LogInformation("Opening browser for pairing: {Url}", pairUrl);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pairUrl)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open browser");
            }

            // Poll for approval
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct);

                try
                {
                    var pollResponse = await httpClient.GetAsync(
                        $"{serverUrl}/auth/pair/poll/{code}",
                        ct);

                    if (pollResponse.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        var result = await pollResponse.Content.ReadFromJsonAsync<PollResult>(cancellationToken: ct);
                        if (result?.Token != null)
                        {
                            _logger.LogInformation("Pairing approved");
                            return result.Token;
                        }
                    }
                    else if ((int)pollResponse.StatusCode == 410)
                    {
                        _logger.LogWarning("Pairing code expired");
                        return null;
                    }
                    // 202 = still pending, continue polling
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error polling pairing status");
                }
            }

            return null;
        }

        private static string GenerateCode()
        {
            var bytes = RandomNumberGenerator.GetBytes(6);
            var chars = new char[6];
            for (int i = 0; i < 6; i++)
            {
                chars[i] = CodeChars[bytes[i] % CodeChars.Length];
            }
            return new string(chars);
        }

        private record PollResult(string? Status, string? Token);
    }
}
