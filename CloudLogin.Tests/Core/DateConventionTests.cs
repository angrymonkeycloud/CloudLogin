using System.Reflection;
using System.Text.Json;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using AngryMonkey.CloudLogin.Server.Serialization;

namespace AngryMonkey.CloudLogin.Tests.Core;

/// <summary>
/// The project's date rules, enforced rather than remembered:
/// <list type="number">
/// <item>Persisted instants are UTC; only the display layer converts to the viewer's timezone.</item>
/// <item>A stored date property is named <c>...On</c> — never <c>...At</c>, and never with a
/// <c>Utc</c> suffix, because where a value is stored is a storage fact, not part of its name.</item>
/// </list>
/// A reflection test rather than a review checklist: the original core shipped with
/// <c>CreatedOn</c> beside <c>ExpiresAtUtc</c>, which is exactly the drift this prevents.
/// </summary>
public class DateConventionTests
{
    /// <summary>
    /// Names that are dates but legitimately do not end in <c>On</c>.
    /// <para>
    /// <c>DateOfBirth</c> is a calendar date, not an instant — it has no timezone to convert and
    /// "born on" is already in the name. It is also a <see cref="DateOnly"/>, so the UTC rule
    /// cannot apply to it.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> AllowedNonOnNames = new(StringComparer.Ordinal)
    {
        "DateOfBirth"
    };

    public static IEnumerable<object[]> PersistedDocumentTypes() =>
        typeof(UserDocument).Assembly.GetTypes()
            .Where(type => type.Namespace == "AngryMonkey.CloudLogin.Server.Core.Domain"
                && type.IsClass && !type.IsAbstract && type.IsPublic)
            .Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(PersistedDocumentTypes))]
    public void StoredDateProperties_UseOn_AndNeverAtOrUtc(Type documentType)
    {
        foreach (PropertyInfo property in DateProperties(documentType))
        {
            Assert.False(
                property.Name.EndsWith("At", StringComparison.Ordinal),
                $"{documentType.Name}.{property.Name} ends in 'At'; dates use 'On' (…ExpiresOn, not …ExpiresAt).");

            Assert.False(
                property.Name.Contains("Utc", StringComparison.OrdinalIgnoreCase),
                $"{documentType.Name}.{property.Name} carries a 'Utc' suffix; every stored instant is UTC, so the name must not say so.");

            Assert.True(
                property.Name.EndsWith("On", StringComparison.Ordinal) || AllowedNonOnNames.Contains(property.Name),
                $"{documentType.Name}.{property.Name} is a date but does not end in 'On'.");
        }
    }

    [Fact]
    public void EveryPersistedDocument_IsCovered()
    {
        // Guards the guard: if the namespace moves, the theory above would silently pass on an
        // empty set.
        Assert.NotEmpty(PersistedDocumentTypes());
        Assert.Contains(PersistedDocumentTypes(), row => (Type)row[0] == typeof(SessionFamilyDocument));
    }

    // ── UTC on write ──────────────────────────────────────────────────────────

    private sealed record TimestampCarrier
    {
        public DateTimeOffset Moment { get; init; }
        public DateTimeOffset? OptionalMoment { get; init; }
    }

    [Fact]
    public void Serializing_WritesUtc_WhateverOffsetTheCallerHeld()
    {
        // The same instant expressed in three offsets must persist identically, or range queries
        // and TTL arithmetic would depend on where the writing machine happened to be.
        DateTimeOffset utc = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset plusThree = utc.ToOffset(TimeSpan.FromHours(3));
        DateTimeOffset minusFive = utc.ToOffset(TimeSpan.FromHours(-5));

        string fromUtc = Serialize(new TimestampCarrier { Moment = utc, OptionalMoment = utc });
        string fromPlusThree = Serialize(new TimestampCarrier { Moment = plusThree, OptionalMoment = plusThree });
        string fromMinusFive = Serialize(new TimestampCarrier { Moment = minusFive, OptionalMoment = minusFive });

        Assert.Equal(fromUtc, fromPlusThree);
        Assert.Equal(fromUtc, fromMinusFive);
        Assert.Contains("+00:00", fromUtc);
    }

    [Fact]
    public void RoundTrip_PreservesTheInstant_AndComesBackAsUtc()
    {
        DateTimeOffset local = new(2026, 8, 31, 15, 0, 0, TimeSpan.FromHours(3));

        TimestampCarrier restored = Deserialize(Serialize(new TimestampCarrier { Moment = local }));

        Assert.Equal(local.ToUniversalTime(), restored.Moment);
        Assert.Equal(TimeSpan.Zero, restored.Moment.Offset);
    }

    [Fact]
    public void NullDates_StayNull()
    {
        TimestampCarrier restored = Deserialize(Serialize(new TimestampCarrier
        {
            Moment = DateTimeOffset.UtcNow,
            OptionalMoment = null
        }));

        Assert.Null(restored.OptionalMoment);
    }

    private static string Serialize<T>(T value)
    {
        using MemoryStream stream = (MemoryStream)new ConfigurableCosmosSerializer().ToStream(value);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static TimestampCarrier Deserialize(string json) =>
        new ConfigurableCosmosSerializer().FromStream<TimestampCarrier>(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));

    private static IEnumerable<PropertyInfo> DateProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property =>
            {
                Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                return propertyType == typeof(DateTimeOffset)
                    || propertyType == typeof(DateTime)
                    || propertyType == typeof(DateOnly);
            });
}
