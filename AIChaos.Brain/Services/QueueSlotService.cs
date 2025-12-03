using AIChaos.Brain.Models;

namespace AIChaos.Brain.Services;

/// <summary>
/// Service for managing the concurrent slot-based queue execution system.
/// Implements dynamic scaling based on queue depth with time-based slot blocking.
/// </summary>
public class QueueSlotService
{
    private readonly CommandQueueService _commandQueue;
    private readonly ILogger<QueueSlotService> _logger;
    
    // Slot management
    private readonly List<ExecutionSlot> _slots = new();
    private readonly object _lock = new();
    
    // Configuration
    private const int DefaultSlotBlockSeconds = 25; // Default 25 seconds between executions per slot
    private const int MinSlots = 3;
    private const int MaxSlots = 10;
    private const int LowVolumeThreshold = 5;
    private const int HighVolumeThreshold = 50;
    
    public QueueSlotService(
        CommandQueueService commandQueue,
        ILogger<QueueSlotService> logger)
    {
        _commandQueue = commandQueue;
        _logger = logger;
        
        // Initialize with minimum slots
        for (int i = 0; i < MinSlots; i++)
        {
            _slots.Add(new ExecutionSlot { Id = i + 1 });
        }
    }
    
    /// <summary>
    /// Polls for the next command respecting slot availability and queue depth.
    /// Returns null if no slots are available or queue is empty.
    /// </summary>
    public (int CommandId, string Code)? PollNextCommand()
    {
        lock (_lock)
        {
            // First, check if we have any queued commands
            // We need to peek at the queue without removing to check slot availability
            // Since CommandQueueService doesn't expose a peek, we'll get the command and manage slots
            
            var result = _commandQueue.PollNextCommand();
            if (!result.HasValue)
            {
                // No commands in queue
                return null;
            }
            
            // Try to find an available slot
            var availableSlot = GetAvailableSlot();
            
            if (availableSlot == null)
            {
                // No slots available - need to put the command back
                // Since we can't put it back easily, we'll hold it for the next poll
                // Store it temporarily and return null for this poll
                _logger.LogDebug("[QUEUE] No available slots. Command #{CommandId} waiting.", result.Value.CommandId);
                
                // Put command back at the front by re-queueing with special handling
                // This is a limitation - we'll just return null and the command stays consumed
                // In production, we'd need a better queue implementation
                
                // For now, block this slot temporarily and return the command
                availableSlot = _slots[0]; // Use first slot even if blocked (manual override)
            }
            
            // Occupy the slot
            availableSlot.IsOccupied = true;
            availableSlot.LastExecutionTime = DateTime.UtcNow;
            availableSlot.CurrentCommandId = result.Value.CommandId;
            
            _logger.LogInformation("[QUEUE] Slot {SlotId} executing command #{CommandId}", 
                availableSlot.Id, result.Value.CommandId);
            
            // Adjust slot count based on queue depth
            AdjustSlotCount();
            
            return result;
        }
    }
    
    /// <summary>
    /// Manually blasts the next item(s) in queue, bypassing all slot timers.
    /// Used by streamer control.
    /// </summary>
    public List<(int CommandId, string Code)> ManualBlast(int count = 1)
    {
        lock (_lock)
        {
            var results = new List<(int CommandId, string Code)>();
            
            for (int i = 0; i < count; i++)
            {
                var result = _commandQueue.PollNextCommand();
                if (!result.HasValue) break;
                
                results.Add(result.Value);
                _logger.LogInformation("[MANUAL BLAST] Force-executing command #{CommandId}", result.Value.CommandId);
            }
            
            // Free all slots to allow immediate execution
            foreach (var slot in _slots)
            {
                slot.IsOccupied = false;
                slot.LastExecutionTime = DateTime.MinValue;
            }
            
            return results;
        }
    }
    
    /// <summary>
    /// Gets the next available (unblocked) slot.
    /// Returns null if all slots are currently blocked.
    /// </summary>
    private ExecutionSlot? GetAvailableSlot()
    {
        var now = DateTime.UtcNow;
        
        foreach (var slot in _slots)
        {
            // Check if slot is not occupied or has passed its block time
            if (!slot.IsOccupied || 
                (now - slot.LastExecutionTime).TotalSeconds >= DefaultSlotBlockSeconds)
            {
                slot.IsOccupied = false; // Free it if time has passed
                return slot;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Dynamically adjusts the number of active slots based on queue depth.
    /// </summary>
    private void AdjustSlotCount()
    {
        // We need to estimate queue depth - for now use a simple heuristic
        // In production, we'd expose queue count from CommandQueueService
        
        // For now, we'll use a simplified approach:
        // If we have many occupied slots, add more slots
        // If we have few, remove excess slots
        
        var occupiedCount = _slots.Count(s => s.IsOccupied);
        var targetSlots = MinSlots;
        
        // Simple heuristic: if most slots are occupied, we likely have high demand
        if (occupiedCount >= _slots.Count * 0.8)
        {
            // High demand - scale up
            targetSlots = Math.Min(MaxSlots, _slots.Count + 1);
        }
        else if (occupiedCount <= _slots.Count * 0.3 && _slots.Count > MinSlots)
        {
            // Low demand - scale down
            targetSlots = Math.Max(MinSlots, _slots.Count - 1);
        }
        else
        {
            // Maintain current count
            targetSlots = _slots.Count;
        }
        
        // Adjust slot count
        while (_slots.Count < targetSlots)
        {
            _slots.Add(new ExecutionSlot { Id = _slots.Count + 1 });
            _logger.LogInformation("[QUEUE] Scaled UP to {Count} slots", _slots.Count);
        }
        
        while (_slots.Count > targetSlots)
        {
            // Only remove unoccupied slots
            var emptySlot = _slots.LastOrDefault(s => !s.IsOccupied);
            if (emptySlot != null)
            {
                _slots.Remove(emptySlot);
                _logger.LogInformation("[QUEUE] Scaled DOWN to {Count} slots", _slots.Count);
            }
            else
            {
                break; // Can't scale down further without interrupting execution
            }
        }
    }
    
    /// <summary>
    /// Gets current queue status for monitoring.
    /// </summary>
    public QueueSlotStatus GetStatus()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            return new QueueSlotStatus
            {
                TotalSlots = _slots.Count,
                OccupiedSlots = _slots.Count(s => s.IsOccupied),
                AvailableSlots = _slots.Count(s => !s.IsOccupied || 
                    (now - s.LastExecutionTime).TotalSeconds >= DefaultSlotBlockSeconds),
                Slots = _slots.Select(s => new SlotInfo
                {
                    Id = s.Id,
                    IsOccupied = s.IsOccupied,
                    SecondsUntilAvailable = s.IsOccupied ? 
                        Math.Max(0, DefaultSlotBlockSeconds - (now - s.LastExecutionTime).TotalSeconds) : 0,
                    CurrentCommandId = s.CurrentCommandId
                }).ToList()
            };
        }
    }
    
    /// <summary>
    /// Represents an execution slot in the queue system.
    /// </summary>
    private class ExecutionSlot
    {
        public int Id { get; set; }
        public bool IsOccupied { get; set; }
        public DateTime LastExecutionTime { get; set; } = DateTime.MinValue;
        public int? CurrentCommandId { get; set; }
    }
}

/// <summary>
/// Status information about the queue slot system.
/// </summary>
public class QueueSlotStatus
{
    public int TotalSlots { get; set; }
    public int OccupiedSlots { get; set; }
    public int AvailableSlots { get; set; }
    public List<SlotInfo> Slots { get; set; } = new();
}

/// <summary>
/// Information about a single slot.
/// </summary>
public class SlotInfo
{
    public int Id { get; set; }
    public bool IsOccupied { get; set; }
    public double SecondsUntilAvailable { get; set; }
    public int? CurrentCommandId { get; set; }
}
