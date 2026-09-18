using Robust.Shared.GameStates;

namespace Content.Shared.DeadSpace.Terminal;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class TerminalComponent : Component
{
    [AutoNetworkedField]
    public int UserIndex;
}
