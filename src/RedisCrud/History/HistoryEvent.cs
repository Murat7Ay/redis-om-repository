using System.Text.Json.Serialization;

namespace RedisCrud.History;

[JsonConverter(typeof(JsonStringEnumConverter<HistoryOperation>))]
public enum HistoryOperation
{
    Create,
    Update,
    Delete,
    Restore
}

/// <summary>
/// One write to an entity, as stored in its history stream.
/// Every write appends exactly one event atomically with the document change,
/// so <see cref="Version"/> values are contiguous within the retained window.
/// </summary>
/// <param name="EventId">Redis stream entry id (time-ordered within the stream).</param>
/// <param name="At">Server time of the write (from the stream entry id).</param>
/// <param name="Operation">What happened.</param>
/// <param name="Version">RowVersion after the write.</param>
/// <param name="Actor">Authenticated subject that performed the write.</param>
/// <param name="Changes">Field-level old/new values. Sensitive fields are masked.</param>
public sealed record HistoryEvent(
    string EventId,
    DateTimeOffset At,
    HistoryOperation Operation,
    int Version,
    string Actor,
    IReadOnlyList<FieldChange> Changes);
