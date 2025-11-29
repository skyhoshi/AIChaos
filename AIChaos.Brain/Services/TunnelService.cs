using System.Diagnostics;
using System.Text.RegularExpressions;
using AIChaos.Brain.Models;

namespace AIChaos.Brain.Services;

/// <summary>
/// Service for managing tunnel connections (ngrok, localtunnel).
/// </summary>
public partial class TunnelService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly ILogger<TunnelService> _logger;
    private readonly HttpClient _httpClient;
    
    private Process? _tunnelProcess;
    
    public bool IsRunning => _tunnelProcess != null && !_tunnelProcess.HasExited;
    public string? CurrentUrl { get; private set; }
    public TunnelType CurrentType { get; private set; } = TunnelType.None;
    public string? PublicIp { get; private set; }
    
    public TunnelService(
        SettingsService settingsService,
        ILogger<TunnelService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _settingsService = settingsService;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }
    
    /// <summary>
    /// Starts an ngrok tunnel.
    /// </summary>
    public async Task<(bool Success, string? Url, string? Error)> StartNgrokAsync(string? authToken = null)
    {
        if (IsRunning)
        {
            return (false, null, "A tunnel is already running. Stop it first.");
        }
        
        try
        {
            // Check if ngrok is installed
            var checkResult = await RunCommandAsync("ngrok", "version");
            if (!checkResult.Success)
            {
                return (false, null, "ngrok is not installed. Please install it from https://ngrok.com/download");
            }
            
            // Configure auth token if provided
            if (!string.IsNullOrEmpty(authToken))
            {
                var authResult = await RunCommandAsync("ngrok", $"config add-authtoken {authToken}");
                if (!authResult.Success)
                {
                    _logger.LogWarning("Failed to set ngrok auth token: {Error}", authResult.Output);
                }
            }
            
            // Start ngrok
            _tunnelProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ngrok",
                    Arguments = "http 5000",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            
            _tunnelProcess.Start();
            _logger.LogInformation("ngrok started with PID {Pid}", _tunnelProcess.Id);
            
            // Wait for ngrok to initialize and get URL from API
            await Task.Delay(3000);
            
            var url = await GetNgrokUrlAsync();
            if (url == null)
            {
                Stop();
                return (false, null, "Failed to get ngrok URL. Make sure ngrok is properly configured.");
            }
            
            CurrentUrl = url;
            CurrentType = TunnelType.Ngrok;
            
            // Update settings
            var tunnel = _settingsService.Settings.Tunnel;
            tunnel.Type = TunnelType.Ngrok;
            tunnel.CurrentUrl = url;
            tunnel.IsRunning = true;
            if (!string.IsNullOrEmpty(authToken))
            {
                tunnel.NgrokAuthToken = authToken;
            }
            _settingsService.UpdateTunnel(tunnel);
            
            // Update Lua file
            await UpdateLuaFileAsync(url);
            
            return (true, url, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ngrok");
            return (false, null, ex.Message);
        }
    }
    
    /// <summary>
    /// Starts a localtunnel.
    /// </summary>
    public async Task<(bool Success, string? Url, string? Error)> StartLocalTunnelAsync()
    {
        if (IsRunning)
        {
            return (false, null, "A tunnel is already running. Stop it first.");
        }
        
        try
        {
            // Check if localtunnel is installed
            var checkResult = await RunCommandAsync("lt", "--version");
            if (!checkResult.Success)
            {
                return (false, null, "localtunnel is not installed. Install it with: npm install -g localtunnel");
            }
            
            // Get public IP for localtunnel password
            await GetPublicIpAsync();
            
            // Start localtunnel
            _tunnelProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "lt",
                    Arguments = "--port 5000",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            
            _tunnelProcess.Start();
            _logger.LogInformation("localtunnel started with PID {Pid}", _tunnelProcess.Id);
            
            // Read output to get URL
            var url = await GetLocalTunnelUrlAsync();
            if (url == null)
            {
                Stop();
                return (false, null, "Failed to get localtunnel URL.");
            }
            
            CurrentUrl = url;
            CurrentType = TunnelType.LocalTunnel;
            
            // Update settings
            var tunnel = _settingsService.Settings.Tunnel;
            tunnel.Type = TunnelType.LocalTunnel;
            tunnel.CurrentUrl = url;
            tunnel.IsRunning = true;
            _settingsService.UpdateTunnel(tunnel);
            
            // Update Lua file
            await UpdateLuaFileAsync(url);
            
            return (true, url, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start localtunnel");
            return (false, null, ex.Message);
        }
    }
    
    /// <summary>
    /// Stops the current tunnel.
    /// </summary>
    public void Stop()
    {
        if (_tunnelProcess != null)
        {
            try
            {
                if (!_tunnelProcess.HasExited)
                {
                    _tunnelProcess.Kill(true);
                    _tunnelProcess.WaitForExit(5000);
                }
                _tunnelProcess.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping tunnel process");
            }
            _tunnelProcess = null;
        }
        
        CurrentUrl = null;
        CurrentType = TunnelType.None;
        
        // Update settings
        var tunnel = _settingsService.Settings.Tunnel;
        tunnel.IsRunning = false;
        tunnel.CurrentUrl = "";
        _settingsService.UpdateTunnel(tunnel);
        
        _logger.LogInformation("Tunnel stopped");
    }
    
    /// <summary>
    /// Gets the current tunnel status.
    /// </summary>
    public TunnelStatus GetStatus()
    {
        return new TunnelStatus
        {
            IsRunning = IsRunning,
            Type = CurrentType,
            Url = CurrentUrl,
            PublicIp = PublicIp
        };
    }
    
    private async Task<string?> GetNgrokUrlAsync()
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                var response = await _httpClient.GetAsync("http://localhost:4040/api/tunnels");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var match = NgrokUrlRegex().Match(json);
                    if (match.Success)
                    {
                        var url = match.Groups[1].Value;
                        // Force HTTPS
                        url = url.Replace("http://", "https://");
                        return url;
                    }
                }
            }
            catch
            {
                // Ignore and retry
            }
            await Task.Delay(1000);
        }
        return null;
    }
    
    private async Task<string?> GetLocalTunnelUrlAsync()
    {
        if (_tunnelProcess == null) return null;
        
        var timeout = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < timeout)
        {
            if (_tunnelProcess.HasExited)
            {
                return null;
            }
            
            var line = await _tunnelProcess.StandardOutput.ReadLineAsync();
            if (!string.IsNullOrEmpty(line))
            {
                _logger.LogDebug("localtunnel output: {Line}", line);
                var match = UrlRegex().Match(line);
                if (match.Success)
                {
                    return match.Value;
                }
            }
        }
        return null;
    }
    
    private async Task GetPublicIpAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://api.ipify.org");
            if (response.IsSuccessStatusCode)
            {
                PublicIp = (await response.Content.ReadAsStringAsync()).Trim();
                return;
            }
        }
        catch { }
        
        try
        {
            var response = await _httpClient.GetAsync("https://icanhazip.com");
            if (response.IsSuccessStatusCode)
            {
                PublicIp = (await response.Content.ReadAsStringAsync()).Trim();
            }
        }
        catch { }
    }
    
    private async Task UpdateLuaFileAsync(string url)
    {
        // Try multiple possible paths to find the Lua file
        var possiblePaths = new[]
        {
            // Relative to the project directory (when running from source)
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "lua", "autorun", "ai_chaos_controller.lua"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "lua", "autorun", "ai_chaos_controller.lua"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "lua", "autorun", "ai_chaos_controller.lua"),
            Path.Combine(AppContext.BaseDirectory, "..", "lua", "autorun", "ai_chaos_controller.lua"),
            // Relative to current working directory
            Path.Combine(Directory.GetCurrentDirectory(), "..", "lua", "autorun", "ai_chaos_controller.lua"),
            Path.Combine(Directory.GetCurrentDirectory(), "lua", "autorun", "ai_chaos_controller.lua"),
        };
        
        string? luaPath = null;
        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                luaPath = fullPath;
                break;
            }
        }
        
        if (luaPath == null)
        {
            _logger.LogWarning("Lua file not found in any expected location. GMod addon should be in ../lua/ relative to AIChaos.Brain");
            _logger.LogWarning("Tunnel URL for GMod: {Url}/poll - Please update your Lua file manually or copy it to the correct location", url);
            return;
        }
        
        try
        {
            var content = await File.ReadAllTextAsync(luaPath);
            var pollUrl = $"{url}/poll";
            var newLine = $"                local SERVER_URL = \"{pollUrl}\" -- Auto-configured by launcher";
            
            content = ServerUrlRegex().Replace(content, newLine);
            
            await File.WriteAllTextAsync(luaPath, content);
            _logger.LogInformation("Updated Lua file with URL: {Url}", pollUrl);
            _logger.LogInformation("Lua file location: {Path}", luaPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Lua file");
        }
    }
    
    private async Task<(bool Success, string Output)> RunCommandAsync(string command, string arguments)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            return (process.ExitCode == 0, output + error);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
    
    [GeneratedRegex(@"""public_url""\s*:\s*""(https?://[^""]+)""")]
    private static partial Regex NgrokUrlRegex();
    
    [GeneratedRegex(@"https://[^\s]+")]
    private static partial Regex UrlRegex();
    
    [GeneratedRegex(@"local SERVER_URL = "".*?"".*")]
    private static partial Regex ServerUrlRegex();
    
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

public class TunnelStatus
{
    public bool IsRunning { get; set; }
    public TunnelType Type { get; set; }
    public string? Url { get; set; }
    public string? PublicIp { get; set; }
}
