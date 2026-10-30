using System.Windows.Input;
using AcmeDeck.Services;

namespace AcmeDeck.ViewModels;

/// <summary>
/// One line in a list of findings: banner, certificate issue or check result.
/// </summary>
public sealed class Issue(CheckLevel level, string message, string? actionText = null, ICommand? action = null)
{
    public CheckLevel Level { get; } = level;
    public string Message { get; } = message;
    public string? ActionText { get; } = actionText;
    public ICommand? Action { get; } = action;
}
