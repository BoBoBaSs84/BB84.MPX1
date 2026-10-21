using BB84.MPX1.App.Services;

namespace BB84.MPX1.App.Tests;

/// <summary>Scripted dialogs: queued confirmation answers, a file to open, and captured saves.</summary>
internal sealed class FakeDialogs : IUserDialogs
{
    public Queue<bool> Answers { get; } = new();

    public List<string> Confirmations { get; } = [];

    public PickedFile? FileToOpen { get; set; }

    public List<(string Name, byte[] Contents)> Saved { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        Confirmations.Add(message);
        return Task.FromResult(Answers.Count > 0 && Answers.Dequeue());
    }

    public Task<PickedFile?> OpenSyxAsync(string title) => Task.FromResult(FileToOpen);

    public Task<string?> SaveSyxAsync(string title, string suggestedName, byte[] contents)
    {
        Saved.Add((suggestedName, contents));
        return Task.FromResult<string?>(suggestedName);
    }
}
