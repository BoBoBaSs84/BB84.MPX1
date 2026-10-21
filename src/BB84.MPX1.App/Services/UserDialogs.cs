using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace BB84.MPX1.App.Services;

/// <summary>A file picked by the user.</summary>
/// <param name="Name">The file name, for messages.</param>
/// <param name="Contents">The file contents.</param>
public sealed record PickedFile(string Name, byte[] Contents);

/// <summary>Dialogs the view models need: confirmations (CON-007) and <c>.syx</c> files (PRG-006).</summary>
public interface IUserDialogs
{
    /// <summary>Asks the user to confirm a destructive operation.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">What will happen.</param>
    /// <param name="confirmText">Text of the confirming button.</param>
    /// <returns><see langword="true"/> when confirmed.</returns>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    /// <summary>Lets the user pick a <c>.syx</c> file to read.</summary>
    /// <param name="title">Dialog title.</param>
    /// <returns>The file, or <see langword="null"/> when cancelled.</returns>
    Task<PickedFile?> OpenSyxAsync(string title);

    /// <summary>Lets the user choose where to save a <c>.syx</c> file and writes it.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="suggestedName">Suggested file name.</param>
    /// <param name="contents">The file contents.</param>
    /// <returns>The saved file name, or <see langword="null"/> when cancelled.</returns>
    Task<string?> SaveSyxAsync(string title, string suggestedName, byte[] contents);
}

/// <summary>Avalonia implementation of <see cref="IUserDialogs"/>, owned by the main window.</summary>
/// <param name="owner">Returns the owner window.</param>
public sealed class AvaloniaUserDialogs(Func<Window?> owner) : IUserDialogs
{
    private static readonly FilePickerFileType SyxType = new("SysEx files (*.syx)") { Patterns = ["*.syx"], MimeTypes = ["application/octet-stream"] };

    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        if (owner() is not { } window)
            return false;

        Window dialog = new()
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        Button confirm = new() { Content = confirmText, Classes = { "accent" } };
        Button cancel = new() { Content = "Cancel", IsCancel = true, IsDefault = true };
        confirm.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, confirm } },
            },
        };
        return await dialog.ShowDialog<bool>(window);
    }

    /// <inheritdoc/>
    public async Task<PickedFile?> OpenSyxAsync(string title)
    {
        if (owner() is not { } window)
            return null;
        IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [SyxType, FilePickerFileTypes.All],
        });
        if (files.Count == 0)
            return null;

        await using Stream stream = await files[0].OpenReadAsync();
        if (stream.CanSeek && stream.Length > Core.Files.SyxFile.MaxSize)
            throw new IOException($"{files[0].Name} is too large for an MPX 1 SysEx file.");
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer);
        return new PickedFile(files[0].Name, buffer.ToArray());
    }

    /// <inheritdoc/>
    public async Task<string?> SaveSyxAsync(string title, string suggestedName, byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        if (owner() is not { } window)
            return null;
        IStorageFile? file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "syx",
            FileTypeChoices = [SyxType],
        });
        if (file is null)
            return null;
        await using Stream stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await stream.WriteAsync(contents);
        return file.Name;
    }
}
