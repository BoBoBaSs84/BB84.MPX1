using System.Runtime.InteropServices;

namespace BB84.MPX1.Midi.Alsa;

/// <summary>
/// Source-generated P/Invoke declarations for the ALSA sequencer in <c>libasound.so.2</c> (TRN-008), plus the few
/// libc calls the reader thread needs.
/// </summary>
internal static unsafe partial class NativeMethods
{
    private const string Asound = "libasound.so.2";

    // shortcut: glibc only (libc.so.6), revisit if a musl build is ever wanted.
    private const string LibC = "libc.so.6";

    public const int SND_SEQ_OPEN_DUPLEX = 3;
    public const int SND_SEQ_NONBLOCK = 1;

    public const int SND_SEQ_CLIENT_SYSTEM = 0;
    public const byte SND_SEQ_ADDRESS_UNKNOWN = 253;
    public const byte SND_SEQ_ADDRESS_SUBSCRIBERS = 254;
    public const byte SND_SEQ_QUEUE_DIRECT = 253;

    public const uint SND_SEQ_PORT_CAP_READ = 1 << 0;
    public const uint SND_SEQ_PORT_CAP_WRITE = 1 << 1;
    public const uint SND_SEQ_PORT_CAP_SUBS_READ = 1 << 5;
    public const uint SND_SEQ_PORT_CAP_SUBS_WRITE = 1 << 6;
    public const uint SND_SEQ_PORT_CAP_NO_EXPORT = 1 << 7;
    public const uint SND_SEQ_PORT_TYPE_MIDI_GENERIC = 1 << 1;
    public const uint SND_SEQ_PORT_TYPE_APPLICATION = 1 << 20;

    public const byte SND_SEQ_EVENT_NONE = 0;
    public const byte SND_SEQ_EVENT_CLOCK = 36;
    public const byte SND_SEQ_EVENT_SENSING = 42;
    public const byte SND_SEQ_EVENT_SYSEX = 130;
    public const byte SND_SEQ_EVENT_LENGTH_VARIABLE = 1 << 2;

    public const int ENOENT = 2;
    public const int EINTR = 4;
    public const int EAGAIN = 11;
    public const int ENOSPC = 28;

    public const short POLLIN = 0x1;
    public const int EFD_CLOEXEC = 0x80000;

    /// <summary><c>snd_seq_event_t</c> on Linux: 28 bytes, 4-byte aligned; <c>snd_seq_ev_ext</c> is packed.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 28)]
    public struct SndSeqEvent
    {
        [FieldOffset(0)] public byte Type;
        [FieldOffset(1)] public byte Flags;
        [FieldOffset(2)] public byte Tag;
        [FieldOffset(3)] public byte Queue;
        [FieldOffset(4)] public ulong Time;
        [FieldOffset(12)] public byte SourceClient;
        [FieldOffset(13)] public byte SourcePort;
        [FieldOffset(14)] public byte DestClient;
        [FieldOffset(15)] public byte DestPort;
        [FieldOffset(16)] public uint ExtLength;
        [FieldOffset(20)] public nint ExtPointer;
    }

    /// <summary><c>struct pollfd</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [LibraryImport(Asound, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int snd_seq_open(nint* handle, string name, int streams, int mode);

    [LibraryImport(Asound)]
    public static partial int snd_seq_close(nint handle);

    [LibraryImport(Asound, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int snd_seq_set_client_name(nint handle, string name);

    [LibraryImport(Asound)]
    public static partial int snd_seq_client_id(nint handle);

    [LibraryImport(Asound, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int snd_seq_create_simple_port(nint handle, string name, uint caps, uint type);

    [LibraryImport(Asound)]
    public static partial int snd_seq_connect_from(nint handle, int myPort, int srcClient, int srcPort);

    [LibraryImport(Asound)]
    public static partial int snd_seq_connect_to(nint handle, int myPort, int destClient, int destPort);

    [LibraryImport(Asound)]
    public static partial int snd_seq_disconnect_from(nint handle, int myPort, int srcClient, int srcPort);

    [LibraryImport(Asound)]
    public static partial int snd_seq_disconnect_to(nint handle, int myPort, int destClient, int destPort);

    [LibraryImport(Asound)]
    public static partial int snd_seq_client_info_malloc(nint* info);

    [LibraryImport(Asound)]
    public static partial void snd_seq_client_info_free(nint info);

    [LibraryImport(Asound)]
    public static partial void snd_seq_client_info_set_client(nint info, int client);

    [LibraryImport(Asound)]
    public static partial int snd_seq_client_info_get_client(nint info);

    [LibraryImport(Asound)]
    public static partial nint snd_seq_client_info_get_name(nint info);

    [LibraryImport(Asound)]
    public static partial int snd_seq_query_next_client(nint handle, nint info);

    [LibraryImport(Asound)]
    public static partial int snd_seq_port_info_malloc(nint* info);

    [LibraryImport(Asound)]
    public static partial void snd_seq_port_info_free(nint info);

    [LibraryImport(Asound)]
    public static partial void snd_seq_port_info_set_client(nint info, int client);

    [LibraryImport(Asound)]
    public static partial void snd_seq_port_info_set_port(nint info, int port);

    [LibraryImport(Asound)]
    public static partial int snd_seq_port_info_get_port(nint info);

    [LibraryImport(Asound)]
    public static partial nint snd_seq_port_info_get_name(nint info);

    [LibraryImport(Asound)]
    public static partial uint snd_seq_port_info_get_capability(nint info);

    [LibraryImport(Asound)]
    public static partial int snd_seq_query_next_port(nint handle, nint info);

    [LibraryImport(Asound)]
    public static partial int snd_seq_event_input(nint handle, SndSeqEvent** ev);

    [LibraryImport(Asound)]
    public static partial int snd_seq_event_output_direct(nint handle, SndSeqEvent* ev);

    [LibraryImport(Asound)]
    public static partial int snd_seq_poll_descriptors_count(nint handle, short events);

    [LibraryImport(Asound)]
    public static partial int snd_seq_poll_descriptors(nint handle, PollFd* fds, uint space, short events);

    [LibraryImport(Asound)]
    public static partial int snd_midi_event_new(nuint bufferSize, nint* parser);

    [LibraryImport(Asound)]
    public static partial void snd_midi_event_free(nint parser);

    [LibraryImport(Asound)]
    public static partial void snd_midi_event_no_status(nint parser, int on);

    [LibraryImport(Asound)]
    public static partial void snd_midi_event_reset_encode(nint parser);

    [LibraryImport(Asound)]
    public static partial nint snd_midi_event_encode(nint parser, byte* buffer, nint count, SndSeqEvent* ev);

    [LibraryImport(Asound)]
    public static partial nint snd_midi_event_decode(nint parser, byte* buffer, nint count, SndSeqEvent* ev);

    [LibraryImport(Asound)]
    public static partial nint snd_strerror(int error);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int poll(PollFd* fds, nuint count, int timeout);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int eventfd(uint initial, int flags);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial nint write(int fd, void* buffer, nuint count);

    [LibraryImport(LibC)]
    public static partial int close(int fd);

    /// <summary>Gets the text for a negative ALSA error code.</summary>
    public static string ErrorText(int error)
        => Marshal.PtrToStringUTF8(snd_strerror(error)) ?? $"error {error}";
}
