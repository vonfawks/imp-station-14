using System.Numerics;
using Content.Shared.CombatMode;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Movement.Events;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Misc;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Impstation.Fishing;

//Imp : Basically a copy of GrapplingGunSystem
public abstract class SharedFishingRodSystem : EntitySystem
{
    [Dependency] protected readonly IGameTiming Timing = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedJointSystem _joints = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public const string GrapplingJoint = "grappling";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CanWeightlessMoveEvent>(OnWeightlessMove);
        SubscribeAllEvent<RequestGrapplingReelMessage>(OnGrapplingReel);

        SubscribeLocalEvent<FishingRodComponent, GunShotEvent>(OnGrapplingShot);
        SubscribeLocalEvent<FishingRodComponent, ActivateInWorldEvent>(OnGunActivate);
        SubscribeLocalEvent<FishingRodComponent, HandDeselectedEvent>(OnGrapplingDeselected);

        SubscribeLocalEvent<FishingProjectileComponent, ProjectileEmbedEvent>(OnGrappleCollide);
        SubscribeLocalEvent<FishingProjectileComponent, JointRemovedEvent>(OnGrappleJointRemoved);
        SubscribeLocalEvent<FishingProjectileComponent, RemoveEmbedEvent>(OnRemoveEmbed);
    }

    private void OnGrappleJointRemoved(EntityUid uid, FishingProjectileComponent component, JointRemovedEvent args)
    {
        if (_netManager.IsServer)
            QueueDel(uid);
    }

    private void OnGrapplingShot(EntityUid uid, FishingRodComponent component, ref GunShotEvent args)
    {
        foreach (var (shotUid, _) in args.Ammo)
        {
            if (!HasComp<FishingProjectileComponent>(shotUid))
                continue;

            //todo: this doesn't actually support multigrapple
            // At least show the visuals.
            component.Projectile = shotUid.Value;
            Dirty(uid, component);
            var visuals = EnsureComp<JointVisualsComponent>(shotUid.Value);
            visuals.Sprite = component.RopeSprite;
            visuals.OffsetA = new Vector2(0f, 0.5f);
            visuals.Target = GetNetEntity(uid);
            Dirty(shotUid.Value, visuals);
        }

        TryComp<AppearanceComponent>(uid, out var appearance);
        _appearance.SetData(uid, SharedTetherGunSystem.TetherVisualsStatus.Key, false, appearance);
        Dirty(uid, component);
    }

    private void OnGrapplingDeselected(EntityUid uid, FishingRodComponent component, HandDeselectedEvent args)
    {
        SetReeling(uid, component, false, args.User);
    }

    private void OnGrapplingReel(RequestGrapplingReelMessage msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } player)
            return;

        if (!_hands.TryGetActiveItem(player, out var activeItem) ||
            !TryComp<FishingRodComponent>(activeItem, out var grappling))
        {
            return;
        }

        if (msg.Reeling &&
            (!TryComp<CombatModeComponent>(player, out var combatMode) ||
             !combatMode.IsInCombatMode))
        {
            return;
        }

        SetReeling(activeItem.Value, grappling, msg.Reeling, player);
    }

    private void OnWeightlessMove(ref CanWeightlessMoveEvent ev)
    {
        if (ev.CanMove || !TryComp<JointRelayTargetComponent>(ev.Uid, out var relayComp))
            return;

        foreach (var relay in relayComp.Relayed)
        {
            if (TryComp<JointComponent>(relay, out var jointRelay) && jointRelay.GetJoints.ContainsKey(GrapplingJoint))
            {
                ev.CanMove = true;
                return;
            }
        }
    }

    private void OnGunActivate(EntityUid uid, FishingRodComponent component, ActivateInWorldEvent args)
    {
        if (!Timing.IsFirstTimePredicted || args.Handled || !args.Complex || component.Projectile is not {} projectile)
            return;

        _audio.PlayPredicted(component.CycleSound, uid, args.User);
        _appearance.SetData(uid, SharedTetherGunSystem.TetherVisualsStatus.Key, true);

        if (_netManager.IsServer)
            QueueDel(projectile);

        component.Projectile = null;
        SetReeling(uid, component, false, args.User);
        _gun.ChangeBasicEntityAmmoCount(uid,  1);

        _joints.RemoveJoint(uid, GrapplingJoint);

        args.Handled = true;
    }

    private void SetReeling(EntityUid uid, FishingRodComponent component, bool value, EntityUid? user)
    {
        if (component.Reeling == value)
            return;

        if (value)
        {
            if (Timing.IsFirstTimePredicted)
                component.Stream = _audio.PlayPredicted(component.ReelSound, uid, user)?.Entity;
        }
        else
        {
            if (Timing.IsFirstTimePredicted)
            {
                component.Stream = _audio.Stop(component.Stream);
            }
        }

        component.Reeling = value;
        Dirty(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FishingRodComponent>();

        while (query.MoveNext(out var uid, out var grappling))
        {
            if (!grappling.Reeling)
            {
                if (Timing.IsFirstTimePredicted)
                {
                    // Just in case.
                    grappling.Stream = _audio.Stop(grappling.Stream);
                }

                continue;
            }

            if (!TryComp<JointComponent>(uid, out var jointComp) ||
                !jointComp.GetJoints.TryGetValue(GrapplingJoint, out var joint) ||
                joint is not DistanceJoint distance)
            {
                SetReeling(uid, grappling, false, null);
                continue;
            }

            // TODO: This should be on engine.
            distance.MaxLength = MathF.Max(distance.MinLength, distance.MaxLength - grappling.ReelRate * frameTime);
            distance.Length = MathF.Min(distance.MaxLength, distance.Length);

            _physics.WakeBody(joint.BodyAUid);
            _physics.WakeBody(joint.BodyBUid);

            if (jointComp.Relay != null)
            {
                _physics.WakeBody(jointComp.Relay.Value);
            }

            Dirty(uid, jointComp);

            if (distance.MaxLength.Equals(distance.MinLength))
            {
                SetReeling(uid, grappling, false, null);
            }
        }
    }

    private void OnGrappleCollide(EntityUid uid, FishingProjectileComponent component, ref ProjectileEmbedEvent args)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        //joint between the embedded and the weapon
        var jointComp = EnsureComp<JointComponent>(args.Weapon);
        var joint = _joints.CreateDistanceJoint(args.Weapon, args.Embedded, anchorA: new Vector2(0f, 0.5f), id: GrapplingJoint);
        joint.MaxLength = joint.Length + 0.2f;
        joint.Stiffness = 1f;
        joint.MinLength = component.JointLength;
        // Setting velocity directly for mob movement fucks this so need to make them aware of it.
        // joint.Breakpoint = 4000f;
        Dirty(args.Weapon, jointComp);
    }

    private void OnRemoveEmbed(EntityUid uid, FishingProjectileComponent component, RemoveEmbedEvent args)
    {
        if (TryComp<EmbeddableProjectileComponent>(uid, out var projectile))
        {
            if (projectile.EmbeddedIntoUid != null)
            {
                _joints.ClearJoints(projectile.EmbeddedIntoUid.Value);
            }
        }
    }

    [Serializable, NetSerializable]
    protected sealed class RequestGrapplingReelMessage : EntityEventArgs
    {
        public bool Reeling;

        public RequestGrapplingReelMessage(bool reeling)
        {
            Reeling = reeling;
        }
    }
}
