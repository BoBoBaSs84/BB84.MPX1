using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using BB84.MPX1.App.ViewModels;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
using BB84.MPX1.Midi.Alsa;
using BB84.MPX1.Midi.WinMM;
using BB84.MPX1.Simulator;

namespace BB84.MPX1.App.Tests;

/// <summary>Checks the constraints, guidelines and patterns of the specification against the code base (spec 3.11 to 3.13).</summary>
[TestClass]
public sealed partial class ArchitectureTests
{
    private static readonly Lazy<string> Root = new(FindRoot);

    private static readonly Assembly Core = typeof(Mpx1Codec).Assembly;
    private static readonly Assembly Midi = typeof(Mpx1Session).Assembly;
    private static readonly Assembly WinMM = typeof(WinMMMidiTransport).Assembly;
    private static readonly Assembly Alsa = typeof(AlsaMidiTransport).Assembly;
    private static readonly Assembly SimulatorAssembly = typeof(Mpx1Simulator).Assembly;
    private static readonly Assembly AppAssembly = typeof(MainWindowViewModel).Assembly;

    private static string FindRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BB84.MPX1.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    private static string ReadFile(string relative) => File.ReadAllText(Path.Combine(Root.Value, relative));

    private static IEnumerable<string> SourceFiles(string directory, params string[] patterns)
        => patterns.SelectMany(p => Directory.EnumerateFiles(Path.Combine(Root.Value, directory), p, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string[] References(Assembly assembly)
        => [.. assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("BB84.", StringComparison.Ordinal) || n.StartsWith("Avalonia", StringComparison.Ordinal)).Order()];

    [TestMethod]
    [Description("CON-001, AC-018: net10.0, the app publishes with Native AOT, and CI runs the AOT publish for win-x64 and linux-x64")]
    public void App_IsNativeAot()
    {
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", ReadFile("Directory.Build.props"));
        Assert.Contains("<PublishAot>true</PublishAot>", ReadFile("src/BB84.MPX1.App/BB84.MPX1.App.csproj"));
        string ci = ReadFile(".github/workflows/ci.yml");
        Assert.MatchesRegex(AotPublish(), ci);
        Assert.Contains("rid: win-x64", ci);
        Assert.Contains("rid: linux-x64", ci);
    }

    [TestMethod]
    [Description("M9: the build is stamped with the version from Directory.Build.props and the window title shows it")]
    public void Version_IsStamped()
    {
        string props = ReadFile("Directory.Build.props");
        int Part(string name) => int.Parse(VersionPart().Matches(props).Single(m => m.Groups[1].Value == name).Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        Version expected = new(Part("Major"), Part("Minor"), Part("Patch"), 0);

        Assert.StartsWith(expected.ToString(3), MainWindowViewModel.Version);
        Assert.Contains(MainWindowViewModel.Version, MainWindowViewModel.Title);
        Assert.AreEqual(expected, AppAssembly.GetName().Version);
    }

    [TestMethod]
    [Description("CON-002: warnings are errors and every library is AOT/trim analysed")]
    public void Build_TreatsTrimWarningsAsErrors()
    {
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", ReadFile("Directory.Build.props"));
        string targets = ReadFile("Directory.Build.targets");
        Assert.Contains("<IsAotCompatible>true</IsAotCompatible>", targets);
        Assert.Contains("<IsTrimmable>true</IsTrimmable>", targets);
    }

    [TestMethod]
    [Description("CON-003: every System.Text.Json call uses source-generated metadata")]
    public void Json_UsesSourceGeneration()
    {
        Regex call = JsonCall();
        List<string> offenders = [];
        foreach (string file in SourceFiles("src", "*.cs"))
        {
            foreach (string line in File.ReadLines(file).Where(l => call.IsMatch(l)))
            {
                // A JsonTypeInfo from a JsonSerializerContext (for example XyzContext.Default.Type) must be passed.
                if (!line.Contains("Context.", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)}: {line.Trim()}");
            }
        }
        Assert.IsEmpty(offenders, string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    [Description("CON-004, PAT-002: ports and adapters; Core has no I/O, OS calls or UI dependency")]
    public void Dependencies_PointInwards()
    {
        Assert.IsEmpty(References(Core), "Core depends on nothing.");
        Assert.AreSequenceEqual(new[] { "BB84.MPX1.Core" }, References(Midi), "Midi depends only on Core.");
        Assert.Contains("BB84.MPX1.Midi", References(WinMM).ToList(), "The WinMM adapter implements the Midi port.");
        string[] adapterAllowed = ["BB84.MPX1.Core", "BB84.MPX1.Midi"];
        CollectionAssert.IsSubsetOf(References(WinMM), adapterAllowed);
        Assert.Contains("BB84.MPX1.Midi", References(Alsa).ToList(), "The ALSA adapter implements the Midi port.");
        CollectionAssert.IsSubsetOf(References(Alsa), adapterAllowed);
        CollectionAssert.IsSubsetOf(References(SimulatorAssembly), adapterAllowed);
        foreach (string file in SourceFiles("src/BB84.MPX1.Core", "*.cs"))
        {
            string text = File.ReadAllText(file);
            foreach (string forbidden in new[] { "File.", "Directory.", "DllImport", "LibraryImport", "Process.", "Socket" })
                Assert.DoesNotContain(forbidden, text, $"{Path.GetFileName(file)} uses {forbidden}");
        }
    }

    [TestMethod]
    [Description("CON-005, COM-001: clean room; nothing from the old editor or help file is part of the sources")]
    public void Sources_ContainNoLegacyMaterial()
    {
        foreach (string file in SourceFiles("src", "*.cs", "*.axaml", "*.csproj", "*.manifest"))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("WazWare", text, file);
            Assert.DoesNotContain("MPX1EDIT", text, file);
            Assert.DoesNotContain("legacy", text, file);
        }

        // The release package refuses legacy and Lexicon material, and the release workflow uses it.
        string package = ReadFile("build/package.ps1");
        Assert.Contains("COM-001", package);
        Assert.Contains("legacy", package);
        Assert.Contains("./build/package.ps1", ReadFile(".github/workflows/release.yml"));
    }

    [TestMethod]
    [Description("CON-006, COM-002: no logo or trademark artwork; the app's only image resource is its own icon")]
    public void Sources_ContainNoArtwork()
    {
        string[] images = [.. SourceFiles("src", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.svg", "*.ico")
            .Select(f => Path.GetRelativePath(Root.Value, f).Replace('\\', '/'))];
        CollectionAssert.AreEqual(new[] { "src/BB84.MPX1.App/Assets/BB84.MPX1.ico" }, images);
    }

    [TestMethod]
    [Description("GUD-001: codec entry points take spans")]
    public void Codec_UsesSpans()
    {
        Assert.AreEqual(typeof(ReadOnlySpan<byte>), typeof(Mpx1Codec).GetMethod(nameof(Mpx1Codec.Decode))!.GetParameters()[0].ParameterType);
        Assert.AreEqual(typeof(ReadOnlySpan<byte>), typeof(Mpx1Codec).GetMethod(nameof(Mpx1Codec.TryDecode))!.GetParameters()[0].ParameterType);
    }

    [TestMethod]
    [Description("GUD-002, PAT-003: messages are a closed set of sealed, immutable records")]
    public void Messages_AreSealedRecords()
    {
        Type[] messages = [.. Core.GetTypes().Where(t => typeof(Mpx1Message).IsAssignableFrom(t) && !t.IsAbstract)];
        Assert.IsGreaterThanOrEqualTo(16, messages.Length);
        foreach (Type type in messages)
        {
            Assert.IsTrue(type.IsSealed, $"{type.Name} must be sealed.");
            Assert.IsNotNull(type.GetMethod("<Clone>$"), $"{type.Name} must be a record.");
            Assert.IsTrue(type.GetProperties().All(p => p.SetMethod is null || p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit")),
                $"{type.Name} must be immutable.");
        }
    }

    [TestMethod]
    [Description("GUD-003: every public async API of the session, transports and simulator takes a CancellationToken")]
    public void AsyncApis_TakeCancellationTokens()
    {
        List<string> offenders = [];
        foreach (Type type in new[] { Midi, WinMM, Alsa, SimulatorAssembly }.SelectMany(a => a.GetExportedTypes()))
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                bool isAsync = method.ReturnType == typeof(Task) || method.ReturnType == typeof(ValueTask)
                    || (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() is var g
                        && (g == typeof(Task<>) || g == typeof(ValueTask<>) || g == typeof(IAsyncEnumerable<>)));
                if (isAsync && method.Name != nameof(IAsyncDisposable.DisposeAsync) && !method.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)))
                    offenders.Add($"{type.Name}.{method.Name}");
            }
        }
        Assert.IsEmpty(offenders, string.Join(", ", offenders));
    }

    [TestMethod]
    [Description("PAT-001: view models use BB84.SourceGenerators and the views use compiled bindings")]
    public void App_UsesMvvmAndCompiledBindings()
    {
        Type[] viewModels = [.. AppAssembly.GetTypes().Where(t => t.Name.EndsWith("ViewModel", StringComparison.Ordinal) && t.IsClass && !t.IsAbstract && t.Namespace!.StartsWith("BB84.MPX1.App.ViewModels", StringComparison.Ordinal))];
        Assert.IsGreaterThanOrEqualTo(10, viewModels.Length);
        foreach (Type type in viewModels.Where(t => t.Name is not ("ModulatorBlockViewModel" or "ControlNodeViewModel" or "SoftRowEntryViewModel" or "PanelButtonViewModel")))
            Assert.IsTrue(typeof(System.ComponentModel.INotifyPropertyChanged).IsAssignableFrom(type), $"{type.Name} must implement INotifyPropertyChanged.");

        Assert.Contains("<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>", ReadFile("src/BB84.MPX1.App/BB84.MPX1.App.csproj"));
        foreach (string view in SourceFiles("src/BB84.MPX1.App/Views", "*.axaml"))
            Assert.Contains("x:CompileBindings=\"True\"", File.ReadAllText(view), Path.GetFileName(view));
    }

    [TestMethod]
    [Description("TRN-001, TRN-008: only the WinMM and ALSA adapters call operating-system MIDI APIs, through source-generated P/Invoke")]
    public void Interop_OnlyInAdapters()
    {
        foreach (string file in SourceFiles("src", "*.cs"))
        {
            string text = File.ReadAllText(file);
            bool inAdapter = file.Contains("BB84.MPX1.Midi.WinMM", StringComparison.Ordinal)
                || file.Contains("BB84.MPX1.Midi.Alsa", StringComparison.Ordinal);
            if (!inAdapter)
                Assert.DoesNotContain("LibraryImport", text, file);
            Assert.DoesNotContain("[DllImport", text, file);
        }
        Assert.Contains("winmm", ReadFile("src/BB84.MPX1.Midi.WinMM/NativeMethods.cs"));
        Assert.Contains("libasound.so.2", ReadFile("src/BB84.MPX1.Midi.Alsa/NativeMethods.cs"));
    }

    [TestMethod]
    [Description("TRN-003: the WinMM input callback is a static UnmanagedCallersOnly function")]
    public void WinMMCallback_IsUnmanagedCallersOnly()
    {
        MethodInfo[] callbacks = [.. WinMM.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            .Where(m => m.GetCustomAttribute<UnmanagedCallersOnlyAttribute>() is not null)];
        Assert.IsNotEmpty(callbacks);
        Assert.IsTrue(callbacks.All(m => m.IsStatic));
    }

    [TestMethod]
    [Description("GUD-005: every requirement ID of the specification is referenced by at least one test")]
    public void EveryRequirement_HasATest()
    {
        string spec = ReadFile("spec/spec-architecture-mpx1-sysex-editor.md");
        string[] ids = [.. RequirementId().Matches(spec).Select(m => m.Value).Distinct().Order()];
        string tests = string.Join('\n', SourceFiles("tests", "*.cs").Select(File.ReadAllText));

        string[] missing = [.. ids.Where(id => !tests.Contains(id, StringComparison.Ordinal))];

        Assert.IsGreaterThanOrEqualTo(100, ids.Length);
        Assert.IsEmpty(missing, $"Requirements without a test: {string.Join(", ", missing)}");
    }

    [GeneratedRegex(@"JsonSerializer\.(Serialize|Deserialize)\(")]
    private static partial Regex JsonCall();

    [GeneratedRegex(@"dotnet publish src/BB84\.MPX1\.App\b[^\n]*-r \$\{\{ matrix\.rid \}\}")]
    private static partial Regex AotPublish();

    [GeneratedRegex(@"<Version(Major|Minor|Patch)>(\d+)</Version\1>")]
    private static partial Regex VersionPart();

    [GeneratedRegex(@"\b(?:PRO|TRN|SES|LRN|PAR|PRG|DMP|SIM|UI|SEC|CON|GUD|PAT|COM)-\d{3}\b|\bAC-\d{3}a?\b")]
    private static partial Regex RequirementId();
}
