using Content.Shared.DeadSpace.Terminal;
using Robust.Server.GameObjects;

namespace Content.Server.Terminal;

public sealed class TerminalSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TerminalComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<TerminalComponent, TerminalCommandMessage>(OnCommand);
    }

    private void OnMapInit(EntityUid uid, TerminalComponent comp, MapInitEvent args)
    {
        comp.UserIndex = Random.Shared.Next(1000, 10000);
        Dirty(uid, comp);
    }

    private void OnCommand(EntityUid uid, TerminalComponent component, TerminalCommandMessage message)
    {
        var command = message.PromptText.Trim();

        var output = command switch
        {
            "" => "\n",
            "help" => "help\nls\nclear\nwhoami\nhostname\n",
            "clear" => "\x01CLEAR",
            "whoami" => $"TEMPUser {component.UserIndex} \n",
            "hostname" => "terminal\n",
            _ => "command not found"
        };

        _ui.SetUiState(uid, TerminalUiKey.Key, new TerminalBoundUserInterfaceState(output));
    }
}
