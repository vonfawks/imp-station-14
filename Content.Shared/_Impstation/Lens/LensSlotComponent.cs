using Robust.Shared.GameStates;

namespace Content.Shared.Lens;

/// <summary>
///     Used alongside <see cref="ItemSlotsComponent"/> to find a specific container.
///     Enables clothing to change functionality based on items inside of it.
/// </summary>
[RegisterComponent]
public sealed partial class LensSlotComponent : Component
{
    [DataField(required: true)]
    public string LensSlotId = string.Empty;
}

/// <summary>
///     Raised directed at an entity with a lens slot when the lens is ejected/inserted.
/// </summary>
public sealed class LensChangedEvent : EntityEventArgs
{
    public readonly bool Ejected;
    public readonly EntityUid Lens;

    public LensChangedEvent(bool ejected, EntityUid lens)
    {
        Ejected = ejected;
        Lens = lens;
    }
}
