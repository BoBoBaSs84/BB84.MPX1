using CommunityToolkit.Mvvm.Input;

using BB84.MPX1.App.Services;
using BB84.MPX1.Core.Files;
using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.App.ViewModels.Editing;

/// <summary><c>.syx</c> file open and save.</summary>
public sealed partial class ProgramEditorViewModel
{
    /// <summary>Opens the first program of a <c>.syx</c> file (UI-010).</summary>
    [RelayCommand]
    public async Task OpenFileAsync()
    {
        try
        {
            PickedFile? file = await _dialogs.OpenSyxAsync("Open a program");
            if (file is null)
                return;
            SyxContent content = SyxFile.Read(file.Contents);
            if (content.Programs.FirstOrDefault() is not { } dump)
            {
                Status = $"{file.Name} contains no program.";
                return;
            }
            ushort? number = dump.ProgramNumber == Request.ActiveProgram ? null : dump.ProgramNumber;
            Load(dump.Program, $"File {file.Name}", number);
            int others = content.Programs.Count() - 1;
            Status = others > 0 ? $"Opened the first of {others + 1} programs in {file.Name}." : $"Opened {file.Name}.";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }
    }

    /// <summary>Saves the program as a <c>.syx</c> file (PRG-006).</summary>
    [RelayCommand]
    public async Task SaveFileAsync()
    {
        ushort number = _sourceNumber is ushort n && n != Request.ActiveProgram ? n : (ushort)StoreTarget;
        byte[] file = SyxFile.Write([new ProgramDump(_device, number, Program)]);
        try
        {
            string? saved = await _dialogs.SaveSyxAsync("Save program", $"{Program.Name.Trim()}.syx", file);
            if (saved is not null)
            {
                Status = $"Saved {saved}.";
                _original = Program;
                IsDirty = false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }
    }
}
