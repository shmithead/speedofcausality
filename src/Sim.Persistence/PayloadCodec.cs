using MessagePack;
using MessagePack.Resolvers;
using Sim.Core.Events;

namespace Sim.Persistence;

/// <summary>
/// Serializes event payloads to MessagePack blobs and back (roadmap §1, §2.1). Each concrete
/// payload type is registered under a <b>stable string tag</b>, so a class rename never invalidates
/// stored events and the tag — not a fragile CLR type name — drives deserialization. Payload schema
/// evolution is handled by registering each version as its own tagged type and upcasting after read
/// (§2.6, <see cref="EventUpcaster"/>).
/// </summary>
public sealed class PayloadCodec
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    private readonly Dictionary<string, Type> _typeByTag = new();
    private readonly Dictionary<Type, string> _tagByType = new();

    /// <summary>Registers a payload type under a stable tag. Returns this for chaining.</summary>
    public PayloadCodec Register<T>(string tag)
        where T : IEventPayload
    {
        _typeByTag[tag] = typeof(T);
        _tagByType[typeof(T)] = tag;
        return this;
    }

    /// <summary>The stable tag for a payload's concrete type.</summary>
    public string TagFor(IEventPayload payload)
    {
        if (!_tagByType.TryGetValue(payload.GetType(), out string? tag))
        {
            throw new InvalidOperationException($"No tag registered for payload type {payload.GetType().Name}.");
        }

        return tag;
    }

    /// <summary>MessagePack bytes for a payload (serialized as its concrete type).</summary>
    public byte[] Serialize(IEventPayload payload)
        => MessagePackSerializer.Serialize(payload.GetType(), payload, Options);

    /// <summary>Reconstructs a payload from its tag and blob.</summary>
    public IEventPayload Deserialize(string tag, byte[] blob)
    {
        if (!_typeByTag.TryGetValue(tag, out Type? type))
        {
            throw new InvalidOperationException($"Unknown payload tag '{tag}'. Register it before reading.");
        }

        return (IEventPayload)MessagePackSerializer.Deserialize(type, blob, Options)!;
    }
}
