using System.Linq;
using Content.Shared.DeadSpace.Terminal;
using Robust.Server.GameObjects;

namespace Content.Server.Terminal;

public sealed class TerminalSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    private sealed class PendingTransfer
    {
        public EntityUid Sender;
        public EntityUid Receiver;
        public float RemainingSeconds;
        public Action Deliver = default!;
    }
    private readonly List<PendingTransfer> _transfers = new();
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TerminalComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<TerminalComponent, TerminalCommandMessage>(OnCommand);
        SubscribeLocalEvent<TerminalComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        for (var i = _transfers.Count - 1; i >= 0; i--)
        {
            var transfer = _transfers[i];
            transfer.RemainingSeconds -= frameTime;

            if (transfer.RemainingSeconds > 0)
                continue;

            _transfers.RemoveAt(i);
            transfer.Deliver();
        }
    }

    private readonly HashSet<string> _ips = new();
    private const float LanSpeedBPS = 100_000f;
    private const float BaseLatencySeconds = 0.05f;
    private const float SignalSpeedTilesPerSecond = 20f;

    private void OnShutdown(EntityUid uid, TerminalComponent comp, ComponentShutdown args)
    {
        _ips.Remove(comp.IpAdress);
    }

    private float GetDistance(EntityUid sender, EntityUid receiver)
    {
        var senderCoords = _transform.GetMapCoordinates(sender);
        var receiverCoords = _transform.GetMapCoordinates(receiver);
        if (senderCoords.MapId != receiverCoords.MapId)
            return float.PositiveInfinity;
        return (senderCoords.Position - receiverCoords.Position).Length();
    }

    private bool TryFindByIp(string address, out EntityUid terminal)
    {
        var query = EntityQueryEnumerator<TerminalComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.IpAdress == address)
            {
                terminal = uid;
                return true;
            }
        }

        terminal = default!;
        return false;
    }

    private float GetTransferDelay(EntityUid sender, EntityUid receiver, int sizeBytes)
    {
        var distance = GetDistance(sender, receiver);
        if (float.IsPositiveInfinity(distance))
            return float.PositiveInfinity;
        return BaseLatencySeconds + distance / SignalSpeedTilesPerSecond + sizeBytes / LanSpeedBPS;
    }

    private void SendWithDelay(EntityUid sender, EntityUid receiver, int sizeBytes, Action deliver)
    {
        var delay = GetTransferDelay(sender, receiver, sizeBytes);

        if (float.IsPositiveInfinity(delay))
            return;
        _transfers.Add(new PendingTransfer
        {
            Sender = sender,
            Receiver = receiver,
            RemainingSeconds = delay,
            Deliver = deliver
        });
    }

    private string GetIp(TerminalComponent comp)
    {
        return comp.IpAdress;
    }
    private void GenerateIp(TerminalComponent comp)
    {
        do
        {
            comp.IpFirst = Random.Shared.Next(0, 256);
            comp.IpSecond = Random.Shared.Next(0, 256);
            comp.IpThird = Random.Shared.Next(0, 256);
        }
        while (!_ips.Add(GetIp(comp)));
    }

    private void OnMapInit(EntityUid uid, TerminalComponent comp, MapInitEvent args)
    {
        GenerateIp(comp);
        comp.UserIndex = Random.Shared.Next(1000, 10000);
        Dirty(uid, comp);
    }

    private void HandlePing(EntityUid sender, string[] args)
    {
        if (args.Length == 0)
        {
            _ui.SetUiState(sender, TerminalUiKey.Key, new TerminalBoundUserInterfaceState($"{Loc.GetString("terminal-ping-usage")}\n"));
            return;
        }
        if (args.Length > 1)
        {
            _ui.SetUiState(sender, TerminalUiKey.Key, new TerminalBoundUserInterfaceState($"{Loc.GetString("terminal-ping-many-args")}\n"));
            return;
        }
        var address = args[0];
        if (!TryFindByIp(address, out var receiver))
        {
            _ui.SetUiState(sender, TerminalUiKey.Key, new TerminalBoundUserInterfaceState($"{Loc.GetString("terminal-ping-host-not-found", ("ip", address))}\n"));
            return;
        }
        if (!TryComp<TerminalComponent>(sender, out var senderComp) || !TryComp<TerminalComponent>(receiver, out var receiverComp))
        {
            _ui.SetUiState(sender, TerminalUiKey.Key, new TerminalBoundUserInterfaceState($"{Loc.GetString("terminal-ping-network-error")}\n"));
            return;
        }
        if (!senderComp.NetworkEnabled || !receiverComp.NetworkEnabled)
        {
            _ui.SetUiState(sender, TerminalUiKey.Key, new TerminalBoundUserInterfaceState($"{Loc.GetString("terminal-ping-host-unreachable")}\n"));
            return;
        }
        var oneWayDelay = GetTransferDelay(sender, receiver, 64);

        if (float.IsPositiveInfinity(oneWayDelay))
        {
            _ui.SetUiState(sender, TerminalUiKey.Key, new TerminalBoundUserInterfaceState($"{Loc.GetString("terminal-ping-host-unreachable")}\n"));
            return;
        }
        var roundTripDelay = oneWayDelay * 2f;
        var milliseconds = Math.Max(1, (int)Math.Round(roundTripDelay * 1000f));

        SendWithDelay(sender, receiver, 64, () =>
        {
            if (!TryComp<TerminalComponent>(receiver, out var currentReceiver) || !currentReceiver.NetworkEnabled)
                return;
            SendWithDelay(receiver, sender, 64, () =>
            {
                if (!TryComp<TerminalComponent>(sender, out var currentSender) || !currentSender.NetworkEnabled)
                    return;
                _ui.SetUiState(sender, TerminalUiKey.Key, new TerminalBoundUserInterfaceState($"64 bytes from {address}: time={milliseconds} ms\n"));
            });
        });
    }

    private void OnCommand(EntityUid uid, TerminalComponent component, TerminalCommandMessage message)
    {
        var parts = message.PromptText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;
        var command = parts[0];
        var arguments = parts.Skip(1).Where(x => !x.StartsWith('-')).ToArray();

        var flags = parts.Skip(1).Where(x => x.StartsWith('-')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (command.Equals("ping", StringComparison.OrdinalIgnoreCase))
        {
            HandlePing(uid, arguments);
            return;
        }
        var output = command switch
        {
            "help" => "help\nls\nclear\nwhoami\nhostname\nifconfig\nping (ip address)\n",
            "clear" => "\x01CLEAR",
            "whoami" => $"User\n",
            "ifconfig" => $"{GetIp(component)}\n",
            "hostname" => $"TEMPUser{component.UserIndex}\n",
            _ => "command not found\n"
        };

        _ui.SetUiState(uid, TerminalUiKey.Key, new TerminalBoundUserInterfaceState(output));
    }
}
