using Rebelgent.Core.Audit;
using Rebelgent.Core.Authority;

namespace Rebelgent.Core.Tests.Audit;

/// <summary>
/// Proves that the canonical JSON form used by <see cref="AuditEvent.ComputeHash"/> is
/// unambiguous. Field boundaries cannot be shifted by user- or agent-controlled content
/// containing pipes, quotes, backslashes, newlines, control characters, Unicode, or JSON.
/// </summary>
public class AuditEventHashCanonicalizationTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    private static string Hash(
        string actorId = "actor",
        string resourceType = "rt",
        string resourceId = "rid",
        string action = "act",
        string payloadJson = "",
        string previousHash = "",
        string eventType = "TaskCreated",
        ActorType actorType = ActorType.Human,
        long sequenceNumber = 1) =>
        AuditEvent.ComputeHash(
            sequenceNumber, FixedTime, eventType, actorType,
            actorId, resourceType, resourceId, action, payloadJson, previousHash);

    [Fact]
    public void PipeInDifferentFields_ProducesDifferentHash()
    {
        // Under the previous pipe-delimited canonicalisation, these two logically distinct
        // events collapsed to the same string. JSON canonicalisation escapes field content
        // so their hashes MUST differ.
        var h1 = Hash(actorId: "a|b", resourceType: "c");
        var h2 = Hash(actorId: "a", resourceType: "b|c");

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void QuoteInField_DoesNotCollideWithAdjacentField()
    {
        var h1 = Hash(actorId: "a\"b", resourceType: "c");
        var h2 = Hash(actorId: "a", resourceType: "\"b\"c");

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void BackslashInField_DoesNotCollideWithAdjacentField()
    {
        var h1 = Hash(actorId: "a\\b", resourceType: "c");
        var h2 = Hash(actorId: "a", resourceType: "\\b\\c");

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void NewlineInField_DoesNotCollideWithAdjacentField()
    {
        var h1 = Hash(actorId: "a\nb", resourceType: "c");
        var h2 = Hash(actorId: "a", resourceType: "b\nc");

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void CarriageReturnInField_DoesNotCollideWithAdjacentField()
    {
        var h1 = Hash(actorId: "a\r\nb", resourceType: "c");
        var h2 = Hash(actorId: "a", resourceType: "b\r\nc");

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void UnicodeInField_ChangesHash()
    {
        var basic = Hash(actorId: "user");
        var unicode = Hash(actorId: "usér");

        Assert.NotEqual(basic, unicode);
    }

    [Fact]
    public void EmptyString_ProducesStableHashDistinctFromNonEmpty()
    {
        var empty = Hash(actorId: "");
        var nonEmpty = Hash(actorId: "x");

        Assert.NotEqual(empty, nonEmpty);
        // Same inputs — hash is deterministic.
        Assert.Equal(empty, Hash(actorId: ""));
    }

    [Fact]
    public void JsonLookingPayload_DoesNotAllowBoundaryConfusion()
    {
        // Payload contains JSON that could look like the outer canonical form.
        var h1 = Hash(payloadJson: "{\"prev\":\"abcd1234\"}", previousHash: "");
        var h2 = Hash(payloadJson: "{}", previousHash: "abcd1234");

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void SameInputs_ProduceSameHash()
    {
        var h1 = Hash(actorId: "user-1", resourceId: "task-42");
        var h2 = Hash(actorId: "user-1", resourceId: "task-42");

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void DifferentSequenceNumber_ProducesDifferentHash()
    {
        var h1 = Hash(sequenceNumber: 1);
        var h2 = Hash(sequenceNumber: 2);

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void DifferentPreviousHash_ProducesDifferentHash()
    {
        var h1 = Hash(previousHash: "aaa");
        var h2 = Hash(previousHash: "bbb");

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void HashIsLowercaseHex64Chars()
    {
        var h = Hash();

        Assert.Equal(64, h.Length);
        Assert.Matches("^[0-9a-f]{64}$", h);
    }
}
