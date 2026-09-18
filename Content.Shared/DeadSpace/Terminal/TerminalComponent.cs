using Robust.Shared.GameStates;

namespace Content.Shared.DeadSpace.Terminal;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class TerminalComponent : Component
{
    [AutoNetworkedField]
    public int UserIndex;

    [AutoNetworkedField]
    public int IpFirst;
    [AutoNetworkedField]
    public int IpSecond;
    [AutoNetworkedField]
    public int IpThird;

    [AutoNetworkedField]
    public string CurrentDir;
    [AutoNetworkedField]
    public string TopDir;

    public string IpAdress => $"204.{IpFirst}.{IpSecond}.{IpThird}";
    public bool NetworkEnabled = true;
}
