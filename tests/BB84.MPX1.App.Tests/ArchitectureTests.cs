using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using CommunityToolkit.Mvvm.ComponentModel;

using BB84.MPX1.App.ViewModels;
using BB84.MPX1.Core.Protocol;
using BB84.MPX1.Midi;
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
    [Description("CON-001, AC-018: net10.0, the app publishes with Native AOT, and CI runs the win-x64 AOT publish")]
    public void App_IsNativeAot()
    {
        StringAssert.Contains(ReadFile("Directory.Build.props"), "<TargetFramework>net10.0</TargetFramework>");
        StringAssert.Contains(ReadFile("src/BB84.MPX1.App/BB84.MPX1.App.csproj"), "<PublishAot>true</PublishAot>");
        StringAssert.Contains(ReadFile(".github/workflows/ci.yml"), "dotnet publish src/BB84.MPX1.App -c Release -r win-x64");
    }

    [TestMethod]
    [Description("M9: the build is stamped with version 1.0.0 and the window title shows it")]
    public void Version_IsStamped()
    {
        StringAssert.StartsWith(MainWindowViewModel.Version, "1.0.0");
        StringAssert.Contains(MainWindowViewModel.Title, MainWindowViewModel.Version);
        Assert.AreEqual(new Version(1, 0, 0, 0), AppAssembly.GetName().Version);
    }

    [TestMethod]
    [Description("CON-002: warnings are errors and every library is AOT/trim analysed")]
    public void Build_TreatsTrimWarningsAsErrors()
    {
        StringAssert.Contains(ReadFile("Directory.Build.props"), "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>");
        string targets = ReadFile("Directory.Build.targets");
        StringAssert.Contains(targets, "<IsAotCompatible>true</IsAotCompatible>");
        StringAssert.Contains(targets, "<IsTrimmable>true</IsTrimmable>");
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
        CollectionAssert.AreEqual(new[] { "BB84.MPX1.Core" }, References(Midi), "Midi depends only on Core.");
        Assert.Contains("BB84.MPX1.Midi", References(WinMM).ToList(), "The WinMM adapter implements the Midi port.");
        string[] adapterAllowed = ["BB84.MPX1.Core", "BB84.MPX1.Midi"];
        CollectionAssert.IsSubsetOf(References(WinMM), adapterAllowed);
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
        StringAssert.Contains(package, "COM-001");
        StringAssert.Contains(package, "legacy");
        StringAssert.Contains(ReadFile(".github/workflows/release.yml"), "./build/package.ps1");
    }

    [TestMethod]
    [Description("CON-006, COM-002: no logo or trademark artwork; the app ships no image resources")]
    public void Sources_ContainNoArtwork()
        => Assert.IsEmpty(SourceFiles("src", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.svg", "*.ico").ToList());

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
        foreach (Type type in new[] { Midi, WinMM, SimulatorAssembly }.SelectMany(a => a.GetExportedTypes()))
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
    [Description("PAT-001: view models use CommunityToolkit.Mvvm and the views use compiled bindings")]
    public void App_UsesMvvmAndCompiledBindings()
    {
        Type[] viewModels = [.. AppAssembly.GetTypes().Where(t => t.Name.EndsWith("ViewModel", StringComparison.Ordinal) && t.IsClass && !t.IsAbstract && t.Namespace!.StartsWith("BB84.MPX1.App.ViewModels", StringComparison.Ordinal))];
        Assert.IsGreaterThanOrEqualTo(10, viewModels.Length);
        foreach (Type type in viewModels.Where(t => t.Name is not ("ModulatorBlockViewModel" or "ControlNodeViewModel" or "SoftRowEntryViewModel" or "PanelButtonViewModel")))
            Assert.IsTrue(typeof(ObservableObject).IsAssignableFrom(type), $"{type.Name} must derive from ObservableObject.");

        StringAssert.Contains(ReadFile("src/BB84.MPX1.App/BB84.MPX1.App.csproj"), "<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>");
        foreach (string view in SourceFiles("src/BB84.MPX1.App/Views", "*.axaml"))
            StringAssert.Contains(File.ReadAllText(view), "x:CompileBindings=\"True\"", Path.GetFileName(view));
    }

    [TestMethod]
    [Description("TRN-001: only the WinMM adapter calls operating-system MIDI APIs, through source-generated P/Invoke")]
    public void Interop_OnlyInWinMMAdapter()
    {
        foreach (string file in SourceFiles("src", "*.cs"))
        {
            string text = File.ReadAllText(file);
            bool inAdapter = file.Contains("BB84.MPX1.Midi.WinMM", StringComparison.Ordinal);
            if (!inAdapter)
                Assert.DoesNotContain("LibraryImport", text, file);
            Assert.DoesNotContain("[DllImport", text, file);
        }
        StringAssert.Contains(ReadFile("src/BB84.MPX1.Midi.WinMM/NativeMethods.cs"), "winmm");
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

    [GeneratedRegex(@"\b(?:PRO|TRN|SES|LRN|PAR|PRG|DMP|SIM|UI|SEC|CON|GUD|PAT|COM)-\d{3}\b|\bAC-\d{3}a?\b")]
    private static partial Regex RequirementId();
}
