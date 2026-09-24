using Content.Server.DeadSpace.Storage.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Storage;

public sealed class RandomSpawnOnUseSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RandomSpawnOnUseComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(EntityUid uid, RandomSpawnOnUseComponent comp, UseInHandEvent args)
    {
        if (args.Handled)
            return;
        if (comp.Items.Count == 0)
        {
            args.Handled = true;
            return;
        }
        var minItems = Math.Max(1, comp.MinItems);
        var maxItems = Math.Max(minItems, comp.MaxItems);
        var amount = _random.Next(minItems, maxItems + 1);
        var coords = Transform(args.User).Coordinates;
        EntityUid? lastSpawned = null;

        for (var i = 0; i < amount; i++)
        {
            var prototype = _random.Pick(comp.Items);
            lastSpawned = Spawn(prototype, coords);
        }
        if (lastSpawned != null)
            _hands.PickupOrDrop(args.User, lastSpawned.Value);
        args.Handled = true;
    }
}
