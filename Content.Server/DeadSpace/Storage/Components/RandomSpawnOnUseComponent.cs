using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Server.DeadSpace.Storage.Components;

[RegisterComponent]
public sealed partial class RandomSpawnOnUseComponent : Component
{
    [DataField("items", required: true)]
    public List<EntProtoId> Items = new();

    [DataField("minItems")]
    public int MinItems = 1;

    [DataField("maxItems")]
    public int MaxItems = 4;

}
