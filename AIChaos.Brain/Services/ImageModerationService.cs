using System.Text.RegularExpressions;
using AIChaos.Brain.Models;

namespace AIChaos.Brain.Services;

/// <summary>
/// Service for moderating images submitted in prompts.
/// </summary>
public class ImageModerationService
{
    private readonly List<PendingImageEntry> _pendingImages = new();
    private readonly SettingsService _settingsService;
    private readonly ILogger<ImageModerationService> _logger;
    private readonly object _lock = new();
    private int _nextId = 1;
    
    // Event for when pending images change
    public event EventHandler? PendingImagesChanged;
    
    // Pattern to match ANY URL
    private static readonly Regex UrlPattern = new(
        @"https?://[^\s]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    public ImageModerationService(
        SettingsService settingsService,
        ILogger<ImageModerationService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }
    
    private void OnPendingImagesChanged()
    {
        PendingImagesChanged?.Invoke(this, EventArgs.Empty);
    }
    
    /// <summary>
    /// Extracts ALL URLs from a prompt.
    /// </summary>
    public List<string> ExtractImageUrls(string prompt)
    {
        var urls = new HashSet<string>();
        
        // Find all URLs
        foreach (Match match in UrlPattern.Matches(prompt))
        {
            var url = match.Value.TrimEnd(')', ']', '>', ',', '.', '!', '?', ';', ':');
            urls.Add(url);
        }
        
        return urls.ToList();
    }
    
    /// <summary>
    /// Checks if a prompt contains URLs that need moderation.
    /// </summary>
    public bool NeedsModeration(string prompt)
    {
        var urls = ExtractImageUrls(prompt);
        return urls.Count > 0;
    }
    
    /// <summary>
    /// Adds a URL to the moderation queue.
    /// </summary>
    public PendingImageEntry AddPendingImage(string imageUrl, string userPrompt, string source, string author, string? userId, int? commandId = null)
    {
        lock (_lock)
        {
            var entry = new PendingImageEntry
            {
                Id = _nextId++,
                CommandId = commandId,
                ImageUrl = imageUrl,
                UserPrompt = userPrompt,
                Source = source,
                Author = author,
                UserId = userId,
                SubmittedAt = DateTime.UtcNow,
                Status = ImageModerationStatus.Pending
            };
            
            _pendingImages.Add(entry);
            _logger.LogInformation("[MODERATION] URL queued for review (Command #{CommandId}): {Url}", commandId, imageUrl);
            
            OnPendingImagesChanged();
            return entry;
        }
    }
    
    /// <summary>
    /// Gets all pending URLs awaiting moderation.
    /// </summary>
    public List<PendingImageEntry> GetPendingImages()
    {
        lock (_lock)
        {
            return _pendingImages
                .Where(i => i.Status == ImageModerationStatus.Pending)
                .OrderBy(i => i.SubmittedAt)
                .ToList();
        }
    }
    
    /// <summary>
    /// Gets all URLs (including reviewed ones).
    /// </summary>
    public List<PendingImageEntry> GetAllImages()
    {
        lock (_lock)
        {
            return new List<PendingImageEntry>(_pendingImages);
        }
    }
    
    /// <summary>
    /// Approves a URL for processing.
    /// </summary>
    public PendingImageEntry? ApproveImage(int imageId)
    {
        lock (_lock)
        {
            var entry = _pendingImages.FirstOrDefault(i => i.Id == imageId);
            if (entry == null) return null;
            
            entry.Status = ImageModerationStatus.Approved;
            entry.ReviewedAt = DateTime.UtcNow;
            _logger.LogInformation("[MODERATION] URL #{Id} APPROVED: {Url}", imageId, entry.ImageUrl);
            
            OnPendingImagesChanged();
            return entry;
        }
    }
    
    /// <summary>
    /// Denies a URL.
    /// </summary>
    public PendingImageEntry? DenyImage(int imageId)
    {
        lock (_lock)
        {
            var entry = _pendingImages.FirstOrDefault(i => i.Id == imageId);
            if (entry == null) return null;
            
            entry.Status = ImageModerationStatus.Denied;
            entry.ReviewedAt = DateTime.UtcNow;
            _logger.LogInformation("[MODERATION] URL #{Id} DENIED: {Url}", imageId, entry.ImageUrl);
            
            OnPendingImagesChanged();
            return entry;
        }
    }
    
    /// <summary>
    /// Gets a URL by ID.
    /// </summary>
    public PendingImageEntry? GetImage(int imageId)
    {
        lock (_lock)
        {
            return _pendingImages.FirstOrDefault(i => i.Id == imageId);
        }
    }
    
    /// <summary>
    /// Cleans up old reviewed URLs (older than 1 hour).
    /// </summary>
    public void CleanupOldEntries()
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddHours(-1);
            _pendingImages.RemoveAll(i => 
                i.Status != ImageModerationStatus.Pending && 
                i.ReviewedAt.HasValue && 
                i.ReviewedAt.Value < cutoff);
        }
    }
    
    /// <summary>
    /// Gets count of pending URLs.
    /// </summary>
    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _pendingImages.Count(i => i.Status == ImageModerationStatus.Pending);
            }
        }
    }
}
