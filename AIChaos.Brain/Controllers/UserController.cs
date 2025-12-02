using AIChaos.Brain.Models;
using AIChaos.Brain.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIChaos.Brain.Controllers;

/// <summary>
/// Controller for user-related API endpoints (credits, submissions).
/// </summary>
[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly UserService _userService;
    private readonly CommandQueueService _commandQueue;
    private readonly AiCodeGeneratorService _codeGenerator;
    private readonly TestClientService _testClientService;
    private readonly ILogger<UserController> _logger;

    public UserController(
        UserService userService,
        CommandQueueService commandQueue,
        AiCodeGeneratorService codeGenerator,
        TestClientService testClientService,
        ILogger<UserController> logger)
    {
        _userService = userService;
        _commandQueue = commandQueue;
        _codeGenerator = codeGenerator;
        _testClientService = testClientService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the credit balance for a user.
    /// </summary>
    [HttpGet("balance")]
    public ActionResult GetBalance([FromHeader(Name = "X-User-Id")] string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest(new { status = "error", message = "User ID required" });
        }

        var user = _userService.GetUser(userId);
        var balance = user?.CreditBalance ?? 0m;
        return Ok(new { balance });
    }

    /// <summary>
    /// Submits a command using user credits.
    /// </summary>
    [HttpPost("submit")]
    public async Task<ActionResult> SubmitCommand(
        [FromHeader(Name = "X-User-Id")] string userId,
        [FromHeader(Name = "X-User-Name")] string? userName,
        [FromBody] UserSubmitRequest request)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest(new { status = "error", message = "User ID required" });
        }

        if (string.IsNullOrEmpty(request.Prompt))
        {
            return BadRequest(new { status = "error", message = "Prompt required" });
        }

        // Check rate limit
        var (allowed, waitSeconds) = _userService.CheckRateLimit(userId);
        if (!allowed)
        {
            return Ok(new
            {
                status = "error",
                message = $"Please wait {waitSeconds:F0} seconds before submitting another command.",
                rateLimited = true,
                waitSeconds
            });
        }

        // Check if user has enough credits
        const decimal commandCost = 0.10m;
        var user = _userService.GetUser(userId);
        var balance = user?.CreditBalance ?? 0m;

        if (balance < commandCost)
        {
            return Ok(new
            {
                status = "error",
                message = $"Insufficient credits. You have ${balance:F2}, but need ${commandCost:F2}",
                balance
            });
        }

        try
        {
            // Generate code
            var (executionCode, undoCode) = await _codeGenerator.GenerateCodeAsync(request.Prompt);

            // Deduct credits
            if (!_userService.DeductCredits(userId, commandCost))
            {
                return Ok(new { status = "error", message = "Failed to deduct credits" });
            }

            // Add to command queue
            var isTestClientModeEnabled = _testClientService.IsEnabled;
            var entry = _commandQueue.AddCommand(
                request.Prompt,
                executionCode,
                undoCode,
                "web",
                userName ?? "User",
                null,
                userId,
                null,
                queueForExecution: !isTestClientModeEnabled);

            // If test client mode is enabled, queue for testing
            if (isTestClientModeEnabled)
            {
                _testClientService.QueueForTesting(entry.Id, executionCode, request.Prompt);
                _logger.LogInformation("[USER] Command #{CommandId} queued for testing by {UserId}", entry.Id, userId);
            }
            else
            {
                _logger.LogInformation("[USER] Command #{CommandId} queued by {UserId}", entry.Id, userId);
            }

            var updatedUser = _userService.GetUser(userId);
            var newBalance = updatedUser?.CreditBalance ?? 0m;
            return Ok(new
            {
                status = "success",
                message = "Command submitted successfully",
                commandId = entry.Id,
                balance = newBalance,
                cost = commandCost
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[USER] Failed to submit command for {UserId}", userId);
            return StatusCode(500, new { status = "error", message = "Failed to process command" });
        }
    }
}

/// <summary>
/// Request model for user command submission.
/// </summary>
public class UserSubmitRequest
{
    public string Prompt { get; set; } = "";
}
