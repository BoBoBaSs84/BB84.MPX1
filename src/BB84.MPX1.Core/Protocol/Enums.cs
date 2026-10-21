namespace BB84.MPX1.Core.Protocol;

/// <summary>MPX 1 SysEx message classes (byte 5 of the header).</summary>
public enum MessageClass : byte
{
    /// <summary>System Configuration (Tx).</summary>
    SystemConfiguration = 0x00,
    /// <summary>Parameter Data (Tx + Rx).</summary>
    ParameterData = 0x01,
    /// <summary>Parameter Display (Tx).</summary>
    ParameterDisplay = 0x02,
    /// <summary>Parameter Type (Tx).</summary>
    ParameterType = 0x03,
    /// <summary>Parameter Description (Tx).</summary>
    ParameterDescription = 0x04,
    /// <summary>Parameter Label (Tx).</summary>
    ParameterLabel = 0x05,
    /// <summary>Request (Rx).</summary>
    Request = 0x06,
    /// <summary>Handshake (Tx + Rx).</summary>
    Handshake = 0x12,
    /// <summary>Database Dump: program sort flags (Tx + Rx).</summary>
    DatabaseDump = 0x16,
    /// <summary>Report Effect Parameters (Tx).</summary>
    EffectParameters = 0x18,
    /// <summary>Report All Effect Parameters (Tx).</summary>
    AllEffectParameters = 0x19,
    /// <summary>Program Information (Tx).</summary>
    ProgramInformation = 0x1A,
    /// <summary>Program Dump (Tx + Rx).</summary>
    ProgramDump = 0x1B,
    /// <summary>Compact Program Dump; Lexicon development use only, passed through raw (PRO-023).</summary>
    CompactProgramDump = 0x1C,
}

/// <summary>Handshake commands (message class <c>12</c>).</summary>
public enum HandshakeCommand : byte
{
    /// <summary>No operation.</summary>
    Nop = 0,
    /// <summary>Connectivity check.</summary>
    AreYouThere = 1,
    /// <summary>Answer to <see cref="AreYouThere"/>.</summary>
    ImAlive = 2,
    /// <summary>Receiver is processing; pause sending.</summary>
    Busy = 3,
    /// <summary>Receiver is ready for more data.</summary>
    Ready = 4,
    /// <summary>Error.</summary>
    Error = 5,
}

/// <summary>Bit flags of a parameter description.</summary>
[Flags]
public enum ParameterAttributes : byte
{
    /// <summary>No flags set.</summary>
    None = 0x00,
    /// <summary>The parameter can be patched.</summary>
    Patchable = 0x01,
    /// <summary>The parameter is transmitted when automation is on.</summary>
    Automation = 0x02,
    /// <summary>The node is a branch (control level) with children.</summary>
    ControlLevel = 0x04,
    /// <summary>The last control level before editable parameters.</summary>
    BottomControlLevel = 0x08,
}

/// <summary>Effect and controller types used by Report Effect Parameters (<c>18</c>) and soft values.</summary>
public enum EffectType : byte
{
    /// <summary>Pitch effect block.</summary>
    Pitch = 0,
    /// <summary>Chorus effect block.</summary>
    Chorus = 1,
    /// <summary>EQ effect block.</summary>
    EQ = 2,
    /// <summary>Modulation effect block.</summary>
    Mod = 3,
    /// <summary>Reverb effect block.</summary>
    Reverb = 4,
    /// <summary>Delay effect block.</summary>
    Delay = 5,
    /// <summary>Knob (custom controller).</summary>
    Knob = 6,
    /// <summary>LFO 1.</summary>
    Lfo1 = 7,
    /// <summary>LFO 2.</summary>
    Lfo2 = 8,
    /// <summary>MIDI arpeggiator.</summary>
    Arpeggiator = 9,
    /// <summary>ADSR 1.</summary>
    Adsr1 = 10,
    /// <summary>ADSR 2.</summary>
    Adsr2 = 11,
    /// <summary>Random number generator.</summary>
    Random = 12,
    /// <summary>A-B generator.</summary>
    AB = 13,
    /// <summary>Sample and hold.</summary>
    SampleAndHold = 14,
    /// <summary>Envelope generator 1.</summary>
    EnvelopeGenerator1 = 15,
    /// <summary>Envelope generator 2.</summary>
    EnvelopeGenerator2 = 16,
}

/// <summary>Effect status bits of a program: set = active, clear = bypassed.</summary>
[Flags]
public enum EffectStatus : byte
{
    /// <summary>All effects bypassed.</summary>
    None = 0x00,
    /// <summary>Pitch on.</summary>
    Pitch = 0x01,
    /// <summary>Chorus on.</summary>
    Chorus = 0x02,
    /// <summary>EQ on.</summary>
    EQ = 0x04,
    /// <summary>Modulation on.</summary>
    Mod = 0x08,
    /// <summary>Reverb on.</summary>
    Reverb = 0x10,
    /// <summary>Delay on.</summary>
    Delay = 0x20,
}

/// <summary>Program "effect type" sort flags (Database Dump, Program Dump).</summary>
[Flags]
public enum EffectCategories : ushort
{
    /// <summary>None.</summary>
    None = 0x0000,
    /// <summary>Pitch.</summary>
    Pitch = 0x0001,
    /// <summary>Chorus.</summary>
    Chorus = 0x0002,
    /// <summary>EQ.</summary>
    EQ = 0x0004,
    /// <summary>Modulation.</summary>
    Mod = 0x0008,
    /// <summary>Delay.</summary>
    Delay = 0x0010,
    /// <summary>Ambient.</summary>
    Ambient = 0x0020,
    /// <summary>Chamber.</summary>
    Chamber = 0x0040,
    /// <summary>Gate.</summary>
    Gate = 0x0080,
    /// <summary>Hall.</summary>
    Hall = 0x0100,
    /// <summary>Inverse.</summary>
    Inverse = 0x0200,
    /// <summary>Plate.</summary>
    Plate = 0x0400,
    /// <summary>Dual.</summary>
    Dual = 0x0800,
}

/// <summary>Program "source type" sort flags (Database Dump, Program Dump).</summary>
[Flags]
public enum SourceCategories : byte
{
    /// <summary>None.</summary>
    None = 0x00,
    /// <summary>Live PA.</summary>
    LivePA = 0x01,
    /// <summary>Vocal.</summary>
    Vocal = 0x02,
    /// <summary>Guitar.</summary>
    Guitar = 0x04,
    /// <summary>Keyboard.</summary>
    Keyboard = 0x08,
    /// <summary>Acoustic.</summary>
    Acoustic = 0x10,
    /// <summary>Drums.</summary>
    Drums = 0x20,
    /// <summary>Tempo.</summary>
    Tempo = 0x40,
    /// <summary>Sound effects.</summary>
    SoundFX = 0x80,
}
