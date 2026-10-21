using BB84.MPX1.Core.Database;
using BB84.MPX1.Core.Programs;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Midi.Learning;

/// <summary>Phase of a learning run.</summary>
public enum LearningPhase
{
    /// <summary>Reading System Configuration.</summary>
    Configuration,
    /// <summary>Requesting parameter descriptions.</summary>
    Descriptions,
    /// <summary>Walking the control tree.</summary>
    Tree,
    /// <summary>Requesting the parameter lists of algorithms and controllers.</summary>
    Algorithms,
    /// <summary>Loaded from cache; nothing was requested.</summary>
    Cached,
    /// <summary>Finished.</summary>
    Done,
}

/// <summary>Progress of a learning run (LRN-003).</summary>
/// <param name="Phase">Current phase.</param>
/// <param name="DescriptionsDone">Descriptions received.</param>
/// <param name="DescriptionsTotal">Descriptions to request.</param>
/// <param name="NodesVisited">Tree nodes found.</param>
/// <param name="Gaps">Addresses that did not answer.</param>
public sealed record LearningProgress(LearningPhase Phase, int DescriptionsDone, int DescriptionsTotal, int NodesVisited, int Gaps);

/// <summary>
/// Learns the parameter descriptions and control tree of a connected MPX 1 (LRN-001 to LRN-004).
/// </summary>
/// <param name="session">A started session.</param>
/// <param name="store">Optional cache; a matching entry is loaded instead of learning.</param>
public sealed class ParameterLearner(Mpx1Session session, IParameterDatabaseStore? store = null)
{
    /// <summary>Upper bound for children of one control level; protects against corrupt limits.</summary>
    public const int MaxChildrenPerNode = 1024;

    private readonly Mpx1Session _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>
    /// Loads the database for the connected firmware from the store, or learns and stores it (LRN-004).
    /// </summary>
    /// <param name="progress">Progress receiver.</param>
    /// <param name="forceRelearn">Ignore a cached entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ParameterDatabase> LoadOrLearnAsync(IProgress<LearningProgress>? progress = null, bool forceRelearn = false, CancellationToken cancellationToken = default)
    {
        progress?.Report(new LearningProgress(LearningPhase.Configuration, 0, 0, 0, 0));
        SystemConfiguration configuration = await _session.RequestAsync<SystemConfiguration>(Request.SystemConfiguration(_session.Device), cancellationToken).ConfigureAwait(false);
        DatabaseKey key = DatabaseKey.From(configuration);

        if (!forceRelearn && store is not null && await store.TryLoadAsync(key, cancellationToken).ConfigureAwait(false) is { } cached)
        {
            progress?.Report(new LearningProgress(LearningPhase.Cached, cached.Descriptions.Count, cached.Descriptions.Count, cached.NodeCount, cached.Gaps.Length));
            return cached;
        }

        ParameterDatabase database = await LearnAsync(configuration, progress, cancellationToken).ConfigureAwait(false);
        if (store is not null)
            await store.SaveAsync(database, cancellationToken).ConfigureAwait(false);
        return database;
    }

    /// <summary>Learns descriptions and tree from the device (LRN-001, LRN-002).</summary>
    /// <param name="configuration">The device's System Configuration.</param>
    /// <param name="progress">Progress receiver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ParameterDatabase> LearnAsync(SystemConfiguration configuration, IProgress<LearningProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ParameterDatabaseBuilder builder = new(DatabaseKey.From(configuration));
        int total = configuration.ParameterTypeCount;
        int gaps = 0;

        for (int type = 0; type < total; type++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParameterDescription description = await _session.RequestAsync<ParameterDescription>(
                Request.ParameterDescription(_session.Device, (ushort)type), cancellationToken).ConfigureAwait(false);
            builder.AddDescription(description);
            if ((type & 0x0F) == 0x0F || type == total - 1)
                progress?.Report(new LearningProgress(LearningPhase.Descriptions, type + 1, total, 0, 0));
        }

        int maxDepth = Math.Max(configuration.MaxControlLevels, (ushort)1);
        await WalkAsync(ControlAddress.Root).ConfigureAwait(false);

        progress?.Report(new LearningProgress(LearningPhase.Algorithms, total, total, builder.NodeCount, gaps));
        await LearnAlgorithmsAsync(builder, cancellationToken).ConfigureAwait(false);

        progress?.Report(new LearningProgress(LearningPhase.Done, total, total, builder.NodeCount, gaps));
        return builder.Build();

        async Task WalkAsync(ControlAddress address)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParameterType type;
            try
            {
                type = await _session.RequestAsync<ParameterType>(Request.ParameterType(_session.Device, address), cancellationToken).ConfigureAwait(false);
            }
            catch (Mpx1TimeoutException) when (!address.IsRoot)
            {
                // A branch's range may contain addresses the unit does not answer; record and continue (spec 9).
                builder.AddGap(address);
                gaps++;
                return;
            }

            builder.AddNode(address, type.Type);
            if (builder.NodeCount % 16 == 0)
                progress?.Report(new LearningProgress(LearningPhase.Tree, total, total, builder.NodeCount, gaps));

            ParameterDescription? description = builder.Describe(type.Type);
            if (description is null || address.Depth >= maxDepth)
                return;

            int children = Math.Min(ParameterDatabaseBuilder.ChildCount(address, description), MaxChildrenPerNode);
            for (int i = 0; i < children; i++)
                await WalkAsync(address.Child((ushort)i)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Requests Report Effect Parameters (<c>18</c>) for every algorithm of the six effect blocks (as many as the
    /// tree shows under <c>A:0 B:effect</c>) and for controller types 6–16, so that program data blocks can be
    /// decoded offline (PRG-003). Unanswered requests are skipped.
    /// </summary>
    private async Task LearnAlgorithmsAsync(ParameterDatabaseBuilder builder, CancellationToken cancellationToken)
    {
        List<(EffectType Effect, byte Algorithm)> requests = [];
        for (int effect = 0; effect < ProgramData.EffectBlockCount; effect++)
        {
            int count = Math.Min(builder.RecordedChildCount(new ControlAddress(0, (ushort)effect)), byte.MaxValue);
            for (int algorithm = 0; algorithm < count; algorithm++)
                requests.Add(((EffectType)effect, (byte)algorithm));
        }
        for (int controller = (int)EffectType.Knob; controller <= (int)EffectType.EnvelopeGenerator2; controller++)
            requests.Add(((EffectType)controller, 0));

        foreach ((EffectType effect, byte algorithm) in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                EffectParameters list = await _session.RequestAsync<EffectParameters>(
                    Request.EffectParameters(_session.Device, effect, algorithm), cancellationToken).ConfigureAwait(false);
                builder.AddAlgorithm(list);
            }
            catch (Mpx1TimeoutException)
            {
                // Not every unit answers for every type; the tree remains the fallback for effect blocks.
            }
        }
    }
}
