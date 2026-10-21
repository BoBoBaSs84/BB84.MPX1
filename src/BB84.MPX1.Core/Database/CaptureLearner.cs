using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Database;

/// <summary>
/// Builds a <see cref="ParameterDatabase"/> from a captured conversation (both directions, in order) instead of a
/// live device (LRN-009). Parameter Type answers carry no address, so each is paired with the most recent
/// Parameter Type request.
/// </summary>
public static class CaptureLearner
{
    /// <summary>Builds the database from decoded messages.</summary>
    /// <param name="messages">Requests and responses in the order they were on the wire.</param>
    /// <exception cref="InvalidOperationException">The capture contains no System Configuration or no root node.</exception>
    public static ParameterDatabase Learn(IEnumerable<Mpx1Message> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        SystemConfiguration? configuration = null;
        List<ParameterDescription> descriptions = [];
        List<(ControlAddress Address, ushort Type)> nodes = [];
        List<EffectParameters> algorithms = [];
        ControlAddress? pendingAddress = null;

        foreach (Mpx1Message message in messages)
        {
            switch (message)
            {
                case SystemConfiguration config:
                    configuration ??= config;
                    break;
                case ParameterDescription description:
                    descriptions.Add(description);
                    break;
                case Request { RequestedClass: MessageClass.ParameterType } request:
                    pendingAddress = request.GetControlAddress();
                    break;
                case EffectParameters list:
                    algorithms.Add(list);
                    break;
                case ParameterType type when pendingAddress is ControlAddress address:
                    nodes.Add((address, type.Type));
                    pendingAddress = null;
                    break;
            }
        }

        if (configuration is null)
            throw new InvalidOperationException("The capture contains no System Configuration message.");

        ParameterDatabaseBuilder builder = new(DatabaseKey.From(configuration));
        foreach (ParameterDescription description in descriptions)
            builder.AddDescription(description);
        foreach ((ControlAddress address, ushort type) in nodes)
            builder.AddNode(address, type);
        foreach (EffectParameters list in algorithms)
            builder.AddAlgorithm(list);
        return builder.Build();
    }

    /// <summary>Builds the database from raw captured SysEx data (for example, a <c>.syx</c> file).</summary>
    /// <param name="data">Concatenated frames; frames that do not decode are skipped.</param>
    public static ParameterDatabase Learn(ReadOnlySpan<byte> data)
    {
        List<Mpx1Message> messages = [];
        foreach (byte[] frame in SysExFrames.Split(data))
        {
            if (Mpx1Codec.TryDecode(frame, out Mpx1Message? message, out _))
                messages.Add(message);
        }
        return Learn(messages);
    }
}
