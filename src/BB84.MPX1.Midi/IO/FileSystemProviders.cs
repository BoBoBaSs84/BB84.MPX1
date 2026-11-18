using BB84.SourceGenerators.Attributes;

namespace BB84.MPX1.Midi.IO;

/// <summary>Injectable wrapper around <see cref="File"/>, so file stores can be tested without touching the disk.</summary>
[GenerateAbstraction(typeof(File), typeof(IFileProvider), typeof(FileProvider))]
public sealed partial class FileProvider;

/// <summary>Abstraction of <see cref="File"/>.</summary>
public partial interface IFileProvider;

/// <summary>Injectable wrapper around <see cref="Directory"/>, so file stores can be tested without touching the disk.</summary>
[GenerateAbstraction(typeof(Directory), typeof(IDirectoryProvider), typeof(DirectoryProvider))]
public sealed partial class DirectoryProvider;

/// <summary>Abstraction of <see cref="Directory"/>.</summary>
public partial interface IDirectoryProvider;
