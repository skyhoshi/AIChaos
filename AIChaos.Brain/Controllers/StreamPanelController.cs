using System.Text.Json;
using AIChaos.Brain.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIChaos.Brain.Controllers;

/// <summary>
/// Controller for stream panel configuration endpoints, including OAuth callbacks.
/// </summary>
[ApiController]
[Route("api/setup")]
public class StreamPanelController : ControllerBase
{
    private readonly SettingsService _settingsService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StreamPanelController> _logger;

    public StreamPanelController(
        SettingsService settingsService,
        IHttpClientFactory httpClientFactory,
        ILogger<StreamPanelController> logger)
    {
        _settingsService = settingsService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Handles the YouTube OAuth callback.
    /// </summary>
    [HttpGet("youtube/callback")]
    public async Task<ActionResult> YouTubeCallback([FromQuery] string? code, [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            return Redirect($"/dashboard/stream?error=YouTube authorization was denied");
        }
        
        if (string.IsNullOrEmpty(code))
        {
            return Redirect($"/dashboard/stream?error=No authorization code received from YouTube");
        }
        
        var settings = _settingsService.Settings.YouTube;
        var redirectUri = $"{Request.Scheme}://{Request.Host}/api/setup/youtube/callback";
        
        try
        {
            var client = _httpClientFactory.CreateClient();
            using (var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            }))
            {
                var tokenResponse = await client.PostAsync(
                    "https://oauth2.googleapis.com/token",
                    content);
                
                if (!tokenResponse.IsSuccessStatusCode)
                {
                    var errorContent = await tokenResponse.Content.ReadAsStringAsync();
                    _logger.LogError("YouTube token exchange failed: {Error}", errorContent);
                    return Redirect($"/dashboard/stream?error=Failed to exchange authorization code for access token");
                }
                
                var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
                var tokenData = JsonDocument.Parse(tokenJson);
                
                settings.AccessToken = tokenData.RootElement.GetProperty("access_token").GetString() ?? "";
                settings.RefreshToken = tokenData.RootElement.TryGetProperty("refresh_token", out var rt)
                    ? rt.GetString() ?? ""
                    : "";
                
                _settingsService.UpdateYouTube(settings);
                _logger.LogInformation("YouTube OAuth successful");
                
                return Redirect("/dashboard/stream?success=YouTube authorization successful! You can now use YouTube features.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YouTube OAuth callback error");
            return Redirect($"/dashboard/stream?error=An error occurred during YouTube authorization: {ex.Message}");
        }
    }
}
