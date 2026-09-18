using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Terminal;

[Serializable, NetSerializable]
public enum TerminalUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class TerminalCommandMessage : BoundUserInterfaceMessage
{
    public string PromptText;

    public TerminalCommandMessage(string promptText)
    {
        PromptText = promptText;
    }
}

[Serializable, NetSerializable]
public sealed class TerminalBoundUserInterfaceState : BoundUserInterfaceState
{
    public string OutputText { get; }

    public TerminalBoundUserInterfaceState(string outputText)
    {
        OutputText = outputText;
    }
}
