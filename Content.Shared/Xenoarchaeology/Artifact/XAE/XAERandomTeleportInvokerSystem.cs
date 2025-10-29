using Content.Shared.Popups;
using Content.Shared.Xenoarchaeology.Artifact.Components; //#IMP
using Content.Shared.Xenoarchaeology.Artifact.XAE.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.Xenoarchaeology.Artifact.XAE;

public sealed class XAERandomTeleportInvokerSystem : BaseXAESystem<XAERandomTeleportInvokerComponent>
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <inheritdoc />
    protected override void OnActivated(Entity<XAERandomTeleportInvokerComponent> ent, ref XenoArtifactNodeActivatedEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;
        // todo: teleport person who activated artifact with artifact itself
        var component = ent.Comp;

        if (!TryComp<XenoArtifactNodeComponent>(ent.Owner, out var nodeComponent) || nodeComponent.Attached is not { } artifact) //#IMP Bugfix: teleport the artifact, not the node
            return;

        var xform = Transform(artifact); //#IMP Bugfix: teleport the artifact, not the node - ent.Owner changed to artifact
        _popup.PopupPredictedCoordinates(Loc.GetString("blink-artifact-popup"), xform.Coordinates, null, PopupType.Medium); //#IMP Bugfix: Only display the popup once!

        var offsetTo = _random.NextVector2(component.MinRange, component.MaxRange);
        _xform.SetCoordinates(artifact, xform, xform.Coordinates.Offset(offsetTo)); //#IMP Bugfix: teleport the artifact, not the node - ent.Owner changed to artifact
    }
}
