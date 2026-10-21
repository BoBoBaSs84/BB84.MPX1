using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi;

/// <summary>
/// Builds predicates that recognise the response to a <see cref="Request"/> by class and, where the
/// response carries it, by address, parameter type, effect or program number (SES-002).
/// </summary>
public static class ResponseMatcher
{
    /// <summary>Returns a predicate that accepts the response to <paramref name="request"/>.</summary>
    /// <param name="request">The request.</param>
    public static Func<Mpx1Message, bool> For(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        DeviceId device = request.Device;
        Func<LexiconMessage, bool> body = request.RequestedClass switch
        {
            MessageClass.ParameterData => ForAddress<ParameterData>(request.GetControlAddress(), m => m.Address),
            MessageClass.ParameterDisplay => ForAddress<ParameterDisplay>(request.GetControlAddress(), m => m.Address),
            MessageClass.ParameterLabel => ForAddress<ParameterLabel>(request.GetControlAddress(), m => m.Address),
            MessageClass.ParameterDescription => ForValue<ParameterDescription>(request.GetWord(0), m => m.Type),
            MessageClass.EffectParameters => m => m is EffectParameters e
                && (byte)e.EffectType == request.Arguments[0]
                && (e.Number == request.Arguments[1] || request.Arguments[0] >= (byte)EffectType.Knob),
            MessageClass.AllEffectParameters => ForValue<AllEffectParameters>(request.GetWord(0), m => m.ProgramNumber),
            MessageClass.ProgramInformation => ForValue<ProgramInformation>(request.GetWord(0), m => m.ProgramNumber),
            MessageClass.ProgramDump => ForValue<ProgramDump>(request.GetWord(0), m => m.ProgramNumber),
            // Parameter Type, System Configuration, Handshake, Database Dump: the response carries no key.
            _ => m => m.MessageClass == request.RequestedClass,
        };

        return message => message is LexiconMessage lexicon
            && (device == DeviceId.All || lexicon.Device == device)
            && body(lexicon);
    }

    private static Func<LexiconMessage, bool> ForAddress<T>(ControlAddress address, Func<T, ControlAddress> select)
        where T : LexiconMessage
        => m => m is T typed && select(typed) == address;

    private static Func<LexiconMessage, bool> ForValue<T>(ushort value, Func<T, ushort> select)
        where T : LexiconMessage
        => m => m is T typed && select(typed) == value;
}
