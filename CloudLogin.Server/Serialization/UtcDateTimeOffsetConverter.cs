using System.Text.Json;
using System.Text.Json.Serialization;

namespace AngryMonkey.CloudLogin.Server.Serialization;

/// <summary>
/// Persists every <see cref="DateTimeOffset"/> in UTC, whatever offset the caller happened to
/// hold.
/// <para>
/// The rule is that stored instants are UTC and only the display layer converts to the viewer's
/// timezone. Enforcing that at the serializer rather than at each assignment means no future
/// property can quietly persist a local-offset value: a document written on a machine in
/// UTC+3 and one written in UTC-5 produce byte-identical timestamps for the same instant, so
/// range queries and TTL arithmetic never depend on where the writer was.
/// </para>
/// <para>
/// This changes representation only, never the instant. Reading is offset-preserving parsing
/// followed by the same normalization, so a value that predates this converter still comes back
/// as the same point in time.
/// </para>
/// </summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime());
}

/// <summary>Nullable counterpart of <see cref="UtcDateTimeOffsetConverter"/>.</summary>
public sealed class NullableUtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value.ToUniversalTime());
    }
}
