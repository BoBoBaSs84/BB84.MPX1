using System.Reflection;

using BB84.MPX1.Midi.IO;

namespace BB84.MPX1.Midi.Tests;

/// <summary>
/// In-memory stand-in for <see cref="IFileProvider"/> and <see cref="IDirectoryProvider"/>. Only the members the
/// file stores use are implemented; every other member throws <see cref="NotSupportedException"/>.
/// </summary>
internal sealed class InMemoryFileSystem
{
    public InMemoryFileSystem()
    {
        File = Proxy<IFileProvider>.Create(this);
        Directory = Proxy<IDirectoryProvider>.Create(this);
    }

    public IFileProvider File { get; }

    public IDirectoryProvider Directory { get; }

    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the calls in order, for example <c>"Move a.tmp b"</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Gets or sets an exception every read throws.</summary>
    public Exception? ReadFailure { get; set; }

    private object? Invoke(Type provider, MethodInfo method, object?[] args)
    {
        string path = (string)args[0]!;
        Calls.Add(method.Name == "Move" ? $"Move {path} {args[1]}" : $"{method.Name} {path}");
        return (provider == typeof(IDirectoryProvider), method.Name) switch
        {
            (true, "CreateDirectory") => CreateDirectory(path),
            (true, "Exists") => Directories.Contains(path),
            (false, "Exists") => Files.ContainsKey(path),
            (false, "ReadAllText") => Read(path),
            (false, "ReadAllTextAsync") => Task.FromResult(Read(path)),
            (false, "WriteAllText") => Write(path, (string)args[1]!),
            (false, "WriteAllTextAsync") => Write(path, (string)args[1]!) ?? Task.CompletedTask,
            (false, "Move") => Move(path, (string)args[1]!),
            _ => throw new NotSupportedException($"{provider.Name}.{method.Name} is not faked."),
        };
    }

    private DirectoryInfo CreateDirectory(string path)
    {
        Directories.Add(path);
        return new DirectoryInfo(path);
    }

    private string Read(string path)
    {
        if (ReadFailure is not null)
            throw ReadFailure;
        return Files.TryGetValue(path, out string? text) ? text : throw new FileNotFoundException(null, path);
    }

    private object? Write(string path, string text)
    {
        Files[path] = text;
        return null;
    }

    private object? Move(string source, string destination)
    {
        Files[destination] = Files[source];
        Files.Remove(source);
        return null;
    }

    // DispatchProxy derives from the proxy type at runtime, so it must not be sealed.
#pragma warning disable CA1852
    public class Proxy<T> : DispatchProxy where T : class
#pragma warning restore CA1852
    {
        private InMemoryFileSystem _owner = null!;

        internal static T Create(InMemoryFileSystem owner)
        {
            T proxy = DispatchProxy.Create<T, Proxy<T>>();
            ((Proxy<T>)(object)proxy)._owner = owner;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => _owner.Invoke(typeof(T), targetMethod!, args ?? []);
    }
}
