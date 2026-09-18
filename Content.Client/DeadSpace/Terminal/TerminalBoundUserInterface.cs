using Content.Shared.DeadSpace.Terminal;
using Robust.Client.GameStates;
using Robust.Client.UserInterface;

namespace Content.Client.DeadSpace.Terminal;

public sealed class TerminalBoundUserInterface : BoundUserInterface
{
    private TerminalWindow? _window;
    private readonly IEntityManager _entityManager = IoCManager.Resolve<IEntityManager>();

    public TerminalBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindowCenteredLeft<TerminalWindow>();
        _window.CommandEntered += OnCommandEntered;
        _window.FocusInput();
        if (!_entityManager.TryGetComponent<TerminalComponent>(Owner, out var terminal))
            return;
        _window.AddColorfullText($"user@TEMPUser{terminal.UserIndex}:~$ ", "16C60C");
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is TerminalBoundUserInterfaceState terminalState)
            _window?.AddOutput(terminalState.OutputText);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _window != null)
            _window.CommandEntered -= OnCommandEntered;
        base.Dispose(disposing);
    }

    private void OnCommandEntered(string command)
    {
        SendMessage(new TerminalCommandMessage(command));
        if (!_entityManager.TryGetComponent<TerminalComponent>(Owner, out var terminal))
            return;
        _window?.AddOutput($"user@TEMPUser{terminal.UserIndex}:~$ {command}\n");
    }
}
