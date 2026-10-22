using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Dumps;
using BB84.MPX1.Midi.Editing;
using BB84.MPX1.Midi.Learning;
using BB84.MPX1.Midi.Library;

namespace BB84.MPX1.App.Services;

/// <summary>
/// One live connection: transport, session, detected firmware and, once learned, the parameter database and
/// editor. Created by <see cref="ConnectAsync"/>; dispose to close the ports.
/// </summary>
public sealed class DeviceConnection : IAsyncDisposable
{
    private readonly IMidiTransport _transport;
    private readonly IParameterDatabaseStore _store;

    private DeviceConnection(IMidiTransport transport, Mpx1Session session, DeviceVersion version, IParameterDatabaseStore store)
    {
        _transport = transport;
        Session = session;
        Version = version;
        _store = store;
    }

    /// <summary>Gets the session.</summary>
    public Mpx1Session Session { get; }

    /// <summary>Gets the detected firmware (SES-007).</summary>
    public DeviceVersion Version { get; }

    /// <summary>Gets the cache key for the connected firmware build (LRN-004).</summary>
    public DatabaseKey DatabaseKey => DatabaseKey.From(Version.Configuration);

    /// <summary>Gets the learned database, or <see langword="null"/> until loaded or learned.</summary>
    public ParameterDatabase? Database { get; private set; }

    /// <summary>Gets the parameter editor, or <see langword="null"/> until the database is available.</summary>
    public ParameterEditor? Editor { get; private set; }

    /// <summary>Gets the program librarian, or <see langword="null"/> until the database is available.</summary>
    public ProgramLibrarian? Librarian { get; private set; }

    /// <summary>Gets the dump service, or <see langword="null"/> until the database is available.</summary>
    public DumpService? Dumps { get; private set; }

    /// <summary>Gets the front panel controller, or <see langword="null"/> when the firmware has no Panel Button parameter.</summary>
    public FrontPanelController? FrontPanel { get; private set; }

    /// <summary>Gets the active program's algorithm numbers, Pitch through Delay, read when the database was attached.</summary>
    public IReadOnlyList<byte> ActiveAlgorithms { get; private set; } = [];

    /// <summary>
    /// Opens the ports, starts a session and detects the firmware. On failure the transport is closed again.
    /// </summary>
    /// <param name="transport">An unopened transport; owned by the connection from now on.</param>
    /// <param name="input">The input port.</param>
    /// <param name="output">The output port.</param>
    /// <param name="device">The device ID.</param>
    /// <param name="options">Session options.</param>
    /// <param name="store">The learned-database cache.</param>
    /// <param name="onFrame">Optional monitor hook, attached before the first frame is sent (UI-009).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Mpx1TimeoutException">The device did not answer.</exception>
    public static async Task<DeviceConnection> ConnectAsync(
        IMidiTransport transport,
        MidiPortId input,
        MidiPortId output,
        DeviceId device,
        Mpx1SessionOptions options,
        IParameterDatabaseStore store,
        EventHandler<FrameEventArgs>? onFrame = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(store);
        Mpx1Session? session = null;
        try
        {
            await transport.OpenAsync(input, output, cancellationToken).ConfigureAwait(false);
            session = new Mpx1Session(transport, device, options);
            if (onFrame is not null)
                session.FrameTraffic += onFrame;
            session.Start();
            DeviceVersion version = await session.DetectVersionAsync(cancellationToken).ConfigureAwait(false);
            return new DeviceConnection(transport, session, version, store);
        }
        catch
        {
            try
            {
                if (session is not null)
                    await session.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await transport.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    /// <summary>Loads the database for this firmware from the cache, without learning.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when a cached database was found.</returns>
    public async Task<bool> TryLoadCachedAsync(CancellationToken cancellationToken = default)
    {
        ParameterDatabase? cached = await _store.TryLoadAsync(DatabaseKey, cancellationToken).ConfigureAwait(false);
        if (cached is null)
            return false;
        await AttachAsync(cached, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Learns the device (or loads the cache unless <paramref name="force"/>) and stores the result.</summary>
    /// <param name="progress">Progress receiver.</param>
    /// <param name="force">Ignore a cached entry (UI-002 forced re-learn).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ParameterDatabase> LearnAsync(IProgress<LearningProgress>? progress, bool force, CancellationToken cancellationToken = default)
    {
        ParameterLearner learner = new(Session, _store);
        ParameterDatabase database = await learner.LoadOrLearnAsync(progress, force, cancellationToken).ConfigureAwait(false);
        await AttachAsync(database, cancellationToken).ConfigureAwait(false);
        return database;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await Session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            // The ports must close even when the session fails to stop.
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task AttachAsync(ParameterDatabase database, CancellationToken cancellationToken)
    {
        ParameterEditor editor = new(Session, database);
        // Prime the LRN-008 guard so the UI can mark unloaded-algorithm parameters before anything is written.
        ActiveAlgorithms = await editor.RefreshActiveAlgorithmsAsync(cancellationToken).ConfigureAwait(false);
        Database = database;
        Editor = editor;
        Librarian = new ProgramLibrarian(Session, database);
        Dumps = new DumpService(Session, database);
        FrontPanel = FrontPanelController.FindButton(database) is null ? null : new FrontPanelController(Session, Dumps);
    }
}
