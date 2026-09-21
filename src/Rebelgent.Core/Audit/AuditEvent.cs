using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Rebelgent.Core.Authority;

namespace Rebelgent.Core.Audit;

/// <summary>
/// An immutable audit event in the tamper-evident ledger.
/// Each event is linked to its predecessor via a SHA256 hash chain.
/// </summary>
public sealed class AuditEvent
{
    public Guid Id { get; private set; }

    /// <summary>Monotonically increasing sequence number assigned at persist time.</summary>
    public long SequenceNumber { get; private set; }

    public DateTimeOffset TimestampUtc { get; private set; }
    public string EventType { get; private set; }
    public ActorType ActorType { get; private set; }
    public string ActorId { get; private set; }
    public string ResourceType { get; private set; }
    public string ResourceId { get; private set; }
    public string Action { get; private set; }
    public string PayloadJson { get; private set; }

    /// <summary>Hex SHA256 of the previous event's canonical data. Empty string for the first event.</summary>
    public string PreviousHash { get; private set; }

    /// <summary>Hex SHA256 of this event's canonical data.</summary>
    public string Hash { get; private set; }

    public AuditEvent(
        long sequenceNumber,
        DateTimeOffset timestampUtc,
        string eventType,
        ActorType actorType,
        string actorId,
        string resourceType,
        string resourceId,
        string action,
        string payloadJson,
        string previousHash)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType cannot be empty.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("ActorId cannot be empty.", nameof(actorId));
        ArgumentNullException.ThrowIfNull(payloadJson);
        ArgumentNullException.ThrowIfNull(previousHash);

        Id = Guid.NewGuid();
        SequenceNumber = sequenceNumber;
        TimestampUtc = timestampUtc;
        EventType = eventType;
        ActorType = actorType;
        ActorId = actorId;
        ResourceType = resourceType ?? string.Empty;
        ResourceId = resourceId ?? string.Empty;
        Action = action ?? string.Empty;
        PayloadJson = payloadJson;
        PreviousHash = previousHash;
        Hash = ComputeHash(sequenceNumber, timestampUtc, eventType, actorType, actorId,
            ResourceType, ResourceId, Action, payloadJson, previousHash);
    }

    internal static AuditEvent Reconstitute(
        Guid id,
        long sequenceNumber,
        DateTimeOffset timestampUtc,
        string eventType,
        ActorType actorType,
        string actorId,
        string resourceType,
        string resourceId,
        string action,
        string payloadJson,
        string previousHash,
        string hash)
    {
        return new AuditEvent
        {
            Id = id,
            SequenceNumber = sequenceNumber,
            TimestampUtc = timestampUtc,
            EventType = eventType,
            ActorType = actorType,
            ActorId = actorId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Action = action,
            PayloadJson = payloadJson,
            PreviousHash = previousHash,
            Hash = hash
        };
    }

    private AuditEvent()
    {
        EventType = string.Empty;
        ActorId = string.Empty;
        ResourceType = string.Empty;
        ResourceId = string.Empty;
        Action = string.Empty;
        PayloadJson = string.Empty;
        PreviousHash = string.Empty;
        Hash = string.Empty;
    }

    /// <summary>
    /// Computes a lowercase hex SHA256 hash of the canonical UTF-8 JSON representation.
    /// Every field is written as a distinct JSON property with fixed ordering. JSON escaping
    /// makes field boundaries unambiguous even when user-controlled fields contain <c>"</c>,
    /// <c>\</c>, newlines, control characters, or Unicode. Timestamps are normalised to UTC
    /// ISO-8601 with invariant culture. Enum values are written as their integer form.
    /// </summary>
    public static string ComputeHash(
        long sequenceNumber,
        DateTimeOffset timestampUtc,
        string eventType,
        ActorType actorType,
        string actorId,
        string resourceType,
        string resourceId,
        string action,
        string payloadJson,
        string previousHash)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("seq", sequenceNumber);
            writer.WriteString("ts", timestampUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("evt", eventType);
            writer.WriteNumber("act", (int)actorType);
            writer.WriteString("aid", actorId);
            writer.WriteString("rt", resourceType);
            writer.WriteString("rid", resourceId);
            writer.WriteString("action", action);
            writer.WriteString("payload", payloadJson);
            writer.WriteString("prev", previousHash);
            writer.WriteEndObject();
        }
        var hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
