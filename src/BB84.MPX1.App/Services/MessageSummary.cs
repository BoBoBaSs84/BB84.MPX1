using System.Globalization;

using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.App.Services;

/// <summary>One-line decoded summaries of MPX 1 messages for the MIDI monitor (UI-009).</summary>
public static class MessageSummary
{
    /// <summary>Summarizes a decoded message, or the decode error when decoding failed.</summary>
    /// <param name="message">The message, or <see langword="null"/>.</param>
    /// <param name="error">The decode error, or <see langword="null"/>.</param>
    public static string Describe(Mpx1Message? message, Mpx1FormatError? error)
    {
        if (message is null)
            return error is null ? "Not an MPX 1 frame" : $"Invalid at byte {error.Offset.ToString(CultureInfo.InvariantCulture)}: {error.Reason}";

        return message switch
        {
            Request r => $"Request {r.RequestedClass}{RequestArguments(r)}",
            SystemConfiguration c => $"System Configuration V{c.MajorVersion}.{c.MinorVersion:D2} {c.BuildDate} {c.BuildTime}, {c.ParameterTypeCount} types",
            ParameterData d => $"Parameter Data {d.Address}: {Convert.ToHexString(d.Data.Span[..Math.Min(d.Data.Length, 16)])}{(d.Data.Length > 16 ? $"… ({d.Data.Length} bytes)" : "")}",
            ParameterDisplay d => $"Parameter Display {d.Address}: \"{d.Text.TrimEnd()}\"",
            ParameterType t => $"Parameter Type 0x{t.Type:X4}",
            ParameterDescription d => $"Parameter Description 0x{d.Type:X4} \"{d.Name.Trim()}\", {d.Size} bytes",
            ParameterLabel l => $"Parameter Label {l.Address}: \"{l.Label.Trim()}\"",
            Handshake h => $"Handshake {h.Command}",
            DatabaseDump => "Database Dump",
            EffectParameters e => $"Effect Parameters {e.EffectType} #{e.Number} \"{e.Parameters.Name.Trim()}\"",
            AllEffectParameters a => $"All Effect Parameters {Program(a.ProgramNumber)}",
            ProgramInformation p => $"Program Information {Program(p.ProgramNumber)}",
            ProgramDump p => $"Program Dump {Program(p.ProgramNumber)} \"{p.Program.Name.Trim()}\"",
            RawLexiconMessage raw => $"Class 0x{raw.RawClass:X2} ({raw.RawPayload.Length} raw bytes)",
            DeviceInquiry => "Device Inquiry",
            IdentityReply i => $"Identity Reply V{i.MajorVersion}.{i.MinorVersion:D2}",
            _ => message.GetType().Name,
        };
    }

    private static string RequestArguments(Request request)
    {
        try
        {
            return request.RequestedClass switch
            {
                MessageClass.ParameterData or MessageClass.ParameterDisplay or MessageClass.ParameterType or MessageClass.ParameterLabel
                    => $" {request.GetControlAddress()}",
                MessageClass.ParameterDescription => $" 0x{request.GetWord(0):X4}",
                MessageClass.Handshake when request.Arguments.Length > 0 => $" {(HandshakeCommand)request.Arguments[0]}",
                MessageClass.AllEffectParameters or MessageClass.ProgramInformation or MessageClass.ProgramDump => $" {Program(request.GetWord(0))}",
                _ => "",
            };
        }
        catch (Mpx1FormatException)
        {
            return " (malformed arguments)";
        }
    }

    private static string Program(ushort number)
        => number == Request.ActiveProgram ? "active" : number.ToString(CultureInfo.InvariantCulture);
}
