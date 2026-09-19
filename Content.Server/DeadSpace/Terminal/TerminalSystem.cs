using System.Linq;
using Content.Shared.DeadSpace.Terminal;
using Robust.Server.GameObjects;
using Robust.Shared.Toolshed.Commands.GameTiming;

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
    private readonly Dictionary<EntityUid, Dictionary<string, string>> _files = new();
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TerminalComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<TerminalComponent, TerminalCommandMessage>(OnCommand);
        SubscribeLocalEvent<TerminalComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<TerminalComponent, TerminalSaveFileMessage>(OnSaveFile);
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
    private readonly Dictionary<EntityUid, HashSet<string>> _directories = new();
    private const float LanSpeedBPS = 100_000f;
    private const float BaseLatencySeconds = 0.05f;
    private const float SignalSpeedTilesPerSecond = 20f;

    private void OnShutdown(EntityUid uid, TerminalComponent comp, ComponentShutdown args)
    {
        _ips.Remove(comp.IpAdress);
        _directories.Remove(uid);
        _files.Remove(uid);
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
    private void GenerateDirs(EntityUid uid, TerminalComponent comp)
    {
        comp.CurrentDir = "/";
        _directories[uid] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/",
            "/home",
            "/tmp",
            "/etc",
            "/bin"
        };

        _files[uid] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void OnMapInit(EntityUid uid, TerminalComponent comp, MapInitEvent args)
    {
        GenerateIp(comp);
        GenerateDirs(uid, comp);
        comp.UserIndex = Random.Shared.Next(1000, 10000);
        Dirty(uid, comp);
    }

    private string HandleLS(EntityUid uid, TerminalComponent comp)
    {
        if (!_directories.TryGetValue(uid, out var dirs) || !_files.TryGetValue(uid, out var files))
            return $"{Loc.GetString("terminal-fs-unaviable")}";
        var currentDir = comp.CurrentDir;
        var prefix = currentDir == "/" ? "/" : $"{currentDir}/";

        var directories = dirs.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path != currentDir).Select(path => path[prefix.Length..]).Where(path => !path.Contains('/'));
        var fileNames = files.Keys.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(path => path[prefix.Length..]).Where(path => !path.Contains('/'));

        var entries = directories.Concat(fileNames).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray();

        return entries.Length == 0 ? "\n" : $"{string.Join('\n', entries)}\n";
    }

    private string HandleCat(EntityUid uid, TerminalComponent comp, string[] args)
    {
        if (args.Length == 0)
            return $"{Loc.GetString("terminal-cat-usage")}\n";
        if (args.Length > 1)
            return $"{Loc.GetString("terminal-many-args")}\n";
        if (!_directories.TryGetValue(uid, out var dirs) || !_files.TryGetValue(uid, out var files))
            return $"{Loc.GetString("terminal-fs-unaviable")}\n";

        var filePath = NormalizePath(comp.CurrentDir, args[0]);

        if (dirs.Contains(filePath))
            return $"{Loc.GetString("terminal-is-directory", ("args", args[0]))}";
        if (!files.TryGetValue(filePath, out var content))
            return $"{Loc.GetString("terminal-no-such-file")}";
        if (string.IsNullOrEmpty(content))
            return "\n";
        return content.EndsWith('\n') ? content : $"{content}\n";
    }

    private static string NormalizePath(string currentDir, string path)
    {
        var combinedPath = path.StartsWith('/') ? path : $"{currentDir}/{path}";
        var parts = combinedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>();

        foreach (var part in parts)
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (normalized.Count > 0)
                    normalized.RemoveAt(normalized.Count - 1);
                continue;
            }
            normalized.Add(part);
        }
        return normalized.Count == 0 ? "/" : $"/{string.Join('/', normalized)}";
    }

    private void OnSaveFile(EntityUid uid, TerminalComponent comp, TerminalSaveFileMessage msg)
    {
        if (!_directories.TryGetValue(uid, out var dirs) || !_files.TryGetValue(uid, out var files))
            return;

        var filePath = NormalizePath(comp.CurrentDir, msg.Path);
        var separator = filePath.LastIndexOf('/');
        var parentDir = separator <= 0 ? "/" : filePath[..separator];

        if (!dirs.Contains(parentDir))
            return;
        files[filePath] = msg.Content;

        _ui.SetUiState(uid, TerminalUiKey.Key, new TerminalBoundUserInterfaceState($"{Loc.GetString("terminal-nano-file-saved", ("filePath", filePath))}\n"));
    }

    private string HandleCD(EntityUid uid, TerminalComponent comp, string[] args)
    {
        if (args.Length == 0)
            return $"{Loc.GetString("terminal-cd-usage")}\n";
        if (args.Length > 1)
            return $"{Loc.GetString("terminal-many-args")}\n";
        if (!_directories.TryGetValue(uid, out var dirs))
            return $"{Loc.GetString("terminal-fs-unaviable")}\n";
        var targetDir = NormalizePath(comp.CurrentDir, args[0]);

        if (!dirs.Contains(targetDir))
            return $"{Loc.GetString("terminal-cd-no-such-dir", ("path", args[0]))}\n";

        comp.CurrentDir = targetDir;
        Dirty(uid, comp);
        return string.Empty;
    }

    private string HandleTouch(EntityUid uid, TerminalComponent comp, string[] args)
    {
        if (args.Length == 0)
            return $"{Loc.GetString("terminal-touch-usage")}\n";
        if (args.Length > 1)
            return $"{Loc.GetString("terminal-many-args")}\n";
        if (!_directories.TryGetValue(uid, out var dirs) || !_files.TryGetValue(uid, out var files))
            return $"{Loc.GetString("terminal-fs-unaviable")}\n";
        var filepath = NormalizePath(comp.CurrentDir, args[0]);
        if (dirs.Contains(filepath))
            return $"{Loc.GetString("terminal-is-directory", ("args", args[0]))}";
        var separator = filepath.LastIndexOf('/');
        var parentDir = separator <= 0 ? "/" : filepath[..separator];

        if (!dirs.Contains(parentDir))
            return $"{Loc.GetString("terminal-parent-dir-not-exist", ("args", args[0]))}";
        if (!files.ContainsKey(filepath))
            files[filepath] = string.Empty;
        return string.Empty;
    }

    private string HandleMKDir(EntityUid uid, TerminalComponent comp, string[] args)
    {
        if (args.Length == 0)
            return $"{Loc.GetString("terminal-mkdir-usage")}\n";
        if (args.Length > 1)
            return $"{Loc.GetString("terminal-many-args")}\n";
        if (!_directories.TryGetValue(uid, out var dirs))
            return $"{Loc.GetString("terminal-fs-unaviable")}\n";
        var newDir = NormalizePath(comp.CurrentDir, args[0]);
        if (dirs.Contains(newDir))
            return $"{Loc.GetString("terminal-dir-exists", ("args", args[0]))}\n";
        var parentDir = newDir[..newDir.LastIndexOf('/')];
        if (string.IsNullOrEmpty(parentDir))
            parentDir = "/";
        if (!dirs.Contains(parentDir))
            return $"{Loc.GetString("terminal-parent-doesnt-exist", ("args", args[0]))}\n";
        dirs.Add(newDir);
        return string.Empty;
    }

    private void HandleNano(EntityUid uid, TerminalComponent comp, string[] args)
    {
        if (args.Length == 0)
        {
            _ui.SetUiState(uid, TerminalUiKey.Key, new TerminalBoundUserInterfaceState($"{Loc.GetString("terminal-nano-usage")}\n"));
            return;
        }
        if (args.Length > 1)
        {
            _ui.SetUiState(uid, TerminalUiKey.Key, new TerminalBoundUserInterfaceState($"{Loc.GetString("terminal-many-args")}\n"));
            return;
        }
        if (!_directories.TryGetValue(uid, out var dirs) || !_files.TryGetValue(uid, out var files))
        {
            _ui.SetUiState(uid, TerminalUiKey.Key, new TerminalBoundUserInterfaceState($"{Loc.GetString("terminal-fs-unaviable")}\n"));
            return;
        }
        var filePath = NormalizePath(comp.CurrentDir, args[0]);
        var separator = filePath.LastIndexOf('/');
        var parentDir = separator <= 0 ? "/" : filePath[..separator];

        if (!dirs.Contains(parentDir))
        {
            _ui.SetUiState(uid, TerminalUiKey.Key, new TerminalBoundUserInterfaceState($"{Loc.GetString("terminal-parent-dir-not-exist")}\n"));
            return;
        }

        files.TryGetValue(filePath, out var content);

        _ui.SetUiState(uid, TerminalUiKey.Key, new TerminalBoundUserInterfaceState(string.Empty, filePath, content ?? string.Empty));
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
        if (command.Equals("nano", StringComparison.OrdinalIgnoreCase))
        {
            HandleNano(uid, component, arguments);
            return;
        }
        var output = command switch
        {
            "help" => $"help\nls\nclear\nwhoami\nhostname\nifconfig\nping (IP адрес)\ncd (путь до директории)\npwd\nls\nmkdir (Название создаваемой директории)\ntouch (Название создаваемого файла)\ncat (название файла)\nnano (название файла)",
            "clear" => "\x01CLEAR",
            "whoami" => $"User\n",
            "ifconfig" => $"{GetIp(component)}\n",
            "hostname" => $"TEMPUser{component.UserIndex}\n",
            "pwd" => $"{component.CurrentDir}\n",
            "ls" => HandleLS(uid, component),
            "cd" => HandleCD(uid, component, arguments),
            "mkdir" => HandleMKDir(uid, component, arguments),
            "touch" => HandleTouch(uid, component, arguments),
            "cat" => HandleCat(uid, component, arguments),
            _ => "command not found\n"
        };

        _ui.SetUiState(uid, TerminalUiKey.Key, new TerminalBoundUserInterfaceState(output));
    }
}
