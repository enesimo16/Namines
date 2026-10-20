using System;

namespace Namines.Core.Events
{
    /// <summary>
    /// Namines Flow modülünde bir otomasyon kuralı tetiklendiğinde fırlatılan olay.
    /// </summary>
    public sealed record FlowTriggerExecutedEvent(
        string RuleId,
        string ProjectId,
        string TriggerType,
        string TableName,
        int ActionCount) : IModuleEvent
    {
        public string EventId { get; init; } = Guid.NewGuid().ToString();
        public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Namines Vault modülünde bir veritabanı yedeği alındığında fırlatılan olay.
    /// </summary>
    public sealed record VaultBackupCompletedEvent(
        string BackupId,
        string ProjectId,
        string DatabaseName,
        long SizeBytes,
        bool Success) : IModuleEvent
    {
        public string EventId { get; init; } = Guid.NewGuid().ToString();
        public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Namines Ground modülünde yeni bir yönetilen veritabanı sağlandığında fırlatılan olay.
    /// </summary>
    public sealed record GroundDatabaseProvisionedEvent(
        string DatabaseId,
        string ProjectId,
        string Provider,
        string DatabaseName) : IModuleEvent
    {
        public string EventId { get; init; } = Guid.NewGuid().ToString();
        public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Namines Desk modülünde güvenli SSO oturum devir jetonu doğrulandığında fırlatılan olay.
    /// </summary>
    public sealed record DeskHandoffValidatedEvent(
        string TokenId,
        string UserId,
        bool IsValid) : IModuleEvent
    {
        public string EventId { get; init; } = Guid.NewGuid().ToString();
        public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    }
}
