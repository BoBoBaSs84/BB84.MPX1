using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BB84.MPX1.Midi.WinMM;

/// <summary>
/// Source-generated P/Invoke declarations for <c>winmm.dll</c> (TRN-002).
/// </summary>
internal static unsafe partial class NativeMethods
{
    private const string WinMM = "winmm.dll";

    public const uint MMSYSERR_NOERROR = 0;
    public const uint CALLBACK_NULL = 0x00000000;
    public const uint CALLBACK_FUNCTION = 0x00030000;

    public const uint MIM_OPEN = 0x3C1;
    public const uint MIM_CLOSE = 0x3C2;
    public const uint MIM_DATA = 0x3C3;
    public const uint MIM_LONGDATA = 0x3C4;
    public const uint MIM_ERROR = 0x3C5;
    public const uint MIM_LONGERROR = 0x3C6;

    public const uint MHDR_DONE = 0x00000001;

    public const int MAXPNAMELEN = 32;

    [StructLayout(LayoutKind.Sequential)]
    public struct MidiInCaps
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        public fixed char szPname[MAXPNAMELEN];
        public uint dwSupport;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MidiOutCaps
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        public fixed char szPname[MAXPNAMELEN];
        public ushort wTechnology;
        public ushort wVoices;
        public ushort wNotes;
        public ushort wChannelMask;
        public uint dwSupport;
    }

    [InlineArray(8)]
    public struct Reserved8
    {
        private nint _element;
    }

    /// <summary>
    /// MIDIHDR. Every field this adapter touches (lpData through dwFlags) has the same offset with natural
    /// and byte packing; WinMM accepts both total sizes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MidiHdr
    {
        public nint lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public nint dwUser;
        public uint dwFlags;
        public nint lpNext;
        public nint reserved;
        public uint dwOffset;
        public Reserved8 dwReserved;
    }

    [LibraryImport(WinMM)]
    public static partial uint midiInGetNumDevs();

    [LibraryImport(WinMM)]
    public static partial uint midiInGetDevCapsW(nuint uDeviceID, MidiInCaps* pmic, uint cbmic);

    [LibraryImport(WinMM)]
    public static partial uint midiInOpen(nint* phmi, uint uDeviceID, nint dwCallback, nint dwInstance, uint fdwOpen);

    [LibraryImport(WinMM)]
    public static partial uint midiInPrepareHeader(nint hmi, MidiHdr* pmh, uint cbmh);

    [LibraryImport(WinMM)]
    public static partial uint midiInUnprepareHeader(nint hmi, MidiHdr* pmh, uint cbmh);

    [LibraryImport(WinMM)]
    public static partial uint midiInAddBuffer(nint hmi, MidiHdr* pmh, uint cbmh);

    [LibraryImport(WinMM)]
    public static partial uint midiInStart(nint hmi);

    [LibraryImport(WinMM)]
    public static partial uint midiInStop(nint hmi);

    [LibraryImport(WinMM)]
    public static partial uint midiInReset(nint hmi);

    [LibraryImport(WinMM)]
    public static partial uint midiInClose(nint hmi);

    [LibraryImport(WinMM)]
    public static partial uint midiInGetErrorTextW(uint mmrError, char* pszText, uint cchText);

    [LibraryImport(WinMM)]
    public static partial uint midiOutGetNumDevs();

    [LibraryImport(WinMM)]
    public static partial uint midiOutGetDevCapsW(nuint uDeviceID, MidiOutCaps* pmoc, uint cbmoc);

    [LibraryImport(WinMM)]
    public static partial uint midiOutOpen(nint* phmo, uint uDeviceID, nint dwCallback, nint dwInstance, uint fdwOpen);

    [LibraryImport(WinMM)]
    public static partial uint midiOutPrepareHeader(nint hmo, MidiHdr* pmh, uint cbmh);

    [LibraryImport(WinMM)]
    public static partial uint midiOutUnprepareHeader(nint hmo, MidiHdr* pmh, uint cbmh);

    [LibraryImport(WinMM)]
    public static partial uint midiOutLongMsg(nint hmo, MidiHdr* pmh, uint cbmh);

    [LibraryImport(WinMM)]
    public static partial uint midiOutShortMsg(nint hmo, uint dwMsg);

    [LibraryImport(WinMM)]
    public static partial uint midiOutReset(nint hmo);

    [LibraryImport(WinMM)]
    public static partial uint midiOutClose(nint hmo);

    [LibraryImport(WinMM)]
    public static partial uint midiOutGetErrorTextW(uint mmrError, char* pszText, uint cchText);
}
