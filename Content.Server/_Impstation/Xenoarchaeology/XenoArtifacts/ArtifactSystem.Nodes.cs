using System.Linq;
using Content.Server.Xenoarchaeology.XenoArtifacts.Events;
using Content.Shared.Whitelist;
using Content.Shared.Xenoarchaeology.XenoArtifacts;
using JetBrains.Annotations;
using Robust.Shared.Random;

namespace Content.Server.Xenoarchaeology.XenoArtifacts;

public sealed partial class ArtifactSystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;

    private const int MaxEdgesPerNode = 4;

    private readonly HashSet<int> _usedNodeIds = new();

    private readonly string _defaultTrigger = "TriggerExamine";
    private readonly string _defaultEffect = "EffectBadFeeling";

    /// <summary>
    /// Generate an Artifact tree with fully developed nodes.
    /// </summary>
    /// <param name="artifact"></param>
    /// <param name="allNodes"></param>
    /// <param name="nodesToCreate">The amount of nodes it has.</param>
    private void GenerateArtifactNodeTree(EntityUid artifact, List<ArtifactNode> allNodes, int nodesToCreate)
    {
        if (nodesToCreate < 1)
        {
            Log.Error($"nodesToCreate {nodesToCreate} is less than 1. Aborting artifact tree generation.");
            return;
        }

        _usedNodeIds.Clear();

        var uninitializedNodes = new List<ArtifactNode> { new(){ Id = GetValidNodeId() } };
        var createdNodes = 1;

        while (uninitializedNodes.Count > 0)
        {
            var node = uninitializedNodes[0];
            uninitializedNodes.Remove(node);

            node.Trigger = GetRandomTrigger(artifact, node);
            node.Effect = GetRandomEffect(artifact, node);

            var maxChildren = _random.Next(1, MaxEdgesPerNode - 1);

            for (var i = 0; i < maxChildren; i++)
            {
                if (nodesToCreate <= createdNodes)
                {
                    break;
                }

                var child = new ArtifactNode {Id = GetValidNodeId(), Depth = node.Depth + 1};
                node.Edges.Add(child.Id);
                child.Edges.Add(node.Id);

                uninitializedNodes.Add(child);
                createdNodes++;
            }

            allNodes.Add(node);
        }
    }

    private int GetValidNodeId()
    {
        var id = _random.Next(100, 1000);
        while (_usedNodeIds.Contains(id))
        {
            id = _random.Next(100, 1000);
        }

        _usedNodeIds.Add(id);

        return id;
    }

    //yeah these two functions are near duplicates but i don't
    //want to implement an interface or abstract parent

    private string GetRandomTrigger(EntityUid artifact, ArtifactNode node)
    {
        var allTriggers = _prototype.EnumeratePrototypes<ArtifactTriggerPrototype>()
            .Where(x => _whitelistSystem.IsWhitelistPassOrNull(x.Whitelist, artifact) &&
            _whitelistSystem.IsBlacklistFailOrNull(x.Blacklist, artifact)).ToList();
        var validDepth = allTriggers.Select(x => x.TargetDepth).Distinct().ToList();

        var weights = GetDepthWeights(validDepth, node.Depth);
        var selectedRandomTargetDepth = GetRandomTargetDepth(weights);
        var targetTriggers = allTriggers
            .Where(x => x.TargetDepth == selectedRandomTargetDepth).ToList();

        return GetTriggerIDUsingProb(targetTriggers);
    }

    private string GetRandomEffect(EntityUid artifact, ArtifactNode node)
    {
        var allEffects = _prototype.EnumeratePrototypes<ArtifactEffectPrototype>()
            .Where(x => _whitelistSystem.IsWhitelistPassOrNull(x.Whitelist, artifact) &&
            _whitelistSystem.IsBlacklistFailOrNull(x.Blacklist, artifact)).ToList();
        var validDepth = allEffects.Select(x => x.TargetDepth).Distinct().ToList();

        var weights = GetDepthWeights(validDepth, node.Depth);
        var selectedRandomTargetDepth = GetRandomTargetDepth(weights);
        var targetEffects = allEffects
            .Where(x => x.TargetDepth == selectedRandomTargetDepth).ToList();

        return GetEffectIDUsingProb(targetEffects);
    }

    /// <remarks>
    /// The goal is that the depth that is closest to targetDepth has the highest chance of appearing.
    /// The issue is that we also want some variance, so levels that are +/- 1 should also have a
    /// decent shot of appearing. This function should probably get some tweaking at some point.
    /// </remarks>
    private Dictionary<int, float> GetDepthWeights(IEnumerable<int> depths, int targetDepth)
    {
        // this function is just a normal distribution with a
        // mean of target depth and standard deviation of 0.75
        var weights = new Dictionary<int, float>();
        foreach (var d in depths)
        {
            var w = 10f / (0.75f * MathF.Sqrt(2 * MathF.PI)) * MathF.Pow(MathF.E, -MathF.Pow((d - targetDepth) / 0.75f, 2));
            weights.Add(d, w);
        }
        return weights;
    }

    /// <summary>
    /// Uses a weighted random system to get a random depth.
    /// </summary>
    private int GetRandomTargetDepth(Dictionary<int, float> weights)
    {
        var sum = weights.Values.Sum();
        var accumulated = 0f;

        var rand = _random.NextFloat() * sum;

        foreach (var (key, weight) in weights)
        {
            accumulated += weight;

            if (accumulated >= rand)
            {
                return key;
            }
        }

        return _random.Pick(weights.Keys); //shouldn't happen
    }

    /// <summary>
    /// Selects an effect using the probability weight
    /// </summary>
    private string GetEffectIDUsingProb(IEnumerable<ArtifactEffectPrototype> effectObjects)
    {
        var maxProbID = ""; //Fallback, shouldn't need this
        var maxProb = 0f;
        var totalProb = 0f;

        // First iteration - get the effect with the highest probability as fallback, and sum all the probabilities
        foreach (var effect in effectObjects)
        {
            if (maxProb < effect.EffectProb)
            {
                maxProb = effect.EffectProb;
                maxProbID = effect.ID;
            }
            totalProb += effect.EffectProb;
        }
        var rand = _random.NextFloat(0f, totalProb);
        var accumulator = 0f;

        // Second iteration - subtract current effect probability from total until total is
        foreach (var effect in effectObjects)
        {
            accumulator += effect.EffectProb;
            if (rand < accumulator){
                return effect.ID;
            }
        }

        return maxProbID;
    }

    /// <summary>
    /// Selects an trigger using the probability weight
    /// </summary>
    private string GetTriggerIDUsingProb(IEnumerable<ArtifactTriggerPrototype> triggerObjects)
    {
        var maxProbID = ""; //Fallback, shouldn't need this
        var maxProb = 0f;
        var totalProb = 0f;

        // First iteration - get the trigger with the highest probability as fallback, and sum all the probabilities
        foreach (var trigger in triggerObjects)
        {
            if (maxProb < trigger.TriggerProb)
            {
                maxProb = trigger.TriggerProb;
                maxProbID = trigger.ID;
            }
            totalProb += trigger.TriggerProb;
        }
        var rand = _random.NextFloat(0f, totalProb);
        var accumulator = 0f;

        foreach (var trigger in triggerObjects)
        {
            accumulator += trigger.TriggerProb;
            if (rand < accumulator){
                return trigger.ID;
            }
        }

        return maxProbID;
    }

    /// <summary>
    /// Enter a node: attach the relevant components
    /// </summary>
    private void EnterNode(EntityUid uid, ref ArtifactNode node, ArtifactComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (component.CurrentNodeId != null)
        {
            ExitNode(uid, component);
        }

        component.CurrentNodeId = node.Id;

        //#IMP Attempt to get trigger/effect from string, fall back to defaults if not in prototype
        // Putting defaults in here rather than components because artifact component isn't guaranteed to exist,
        // and these should never be modified in-game

        _prototype.TryIndex<ArtifactTriggerPrototype>(node.Trigger, out var maybeTrigger);
        _prototype.TryIndex<ArtifactEffectPrototype>(node.Effect, out var maybeEffect);

        var trigger = _prototype.Index<ArtifactTriggerPrototype>(_defaultTrigger);
        var effect = _prototype.Index<ArtifactEffectPrototype>(_defaultEffect);
        if (maybeTrigger is null)
            Log.Debug($"Trigger prototype {node.Trigger} not found for artifact entity {ToPrettyString(uid)}, falling back to default");
        else
            trigger = maybeTrigger;

        if (maybeEffect is null)
            Log.Debug($"Effect prototype {node.Effect} not found for artifact entity {ToPrettyString(uid)}, falling back to default");
        else
            effect = maybeEffect;

        // #IMP: Save trigger & effect to allow proper exiting in case admin edits between entry and exit.
        node.StoredTrigger = maybeTrigger != null ? node.Trigger : _defaultTrigger;
        node.StoredEffect = maybeEffect != null ? node.Effect : _defaultEffect;

        //#END IMP

        var allComponents = effect.Components.Concat(effect.PermanentComponents).Concat(trigger.Components);
        foreach (var (name, entry) in allComponents)
        {
            var reg = _componentFactory.GetRegistration(name);

            // Don't re-add permanent components, ever
            if (effect.PermanentComponents.ContainsKey(name) && EntityManager.HasComponent(uid, reg.Type))
                continue;

            if (node.Discovered && EntityManager.HasComponent(uid, reg.Type))
                EntityManager.RemoveComponent(uid, reg.Type);

            var comp = (Component)_componentFactory.GetComponent(reg);

            var temp = (object)comp;
            _serialization.CopyTo(entry.Component, ref temp);
            EntityManager.RemoveComponent(uid, temp!.GetType());
            EntityManager.AddComponent(uid, (Component)temp!);
        }

        node.Discovered = true;
        RaiseLocalEvent(uid, new ArtifactNodeEnteredEvent(component.CurrentNodeId.Value));
    }

    /// <summary>
    /// Exit a node: remove the relevant components.
    /// </summary>
    private void ExitNode(EntityUid uid, ArtifactComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (component.CurrentNodeId == null)
            return;
        var currentNode = GetNodeFromId(component.CurrentNodeId.Value, component);

        var trigger = _prototype.Index<ArtifactTriggerPrototype>(currentNode.StoredTrigger);
        var effect = _prototype.Index<ArtifactEffectPrototype>(currentNode.StoredEffect);

        var entityPrototype = MetaData(uid).EntityPrototype;
        var toRemove = effect.Components.Keys.Concat(trigger.Components.Keys).ToList();

        foreach (var name in toRemove)
        {
            // if the entity prototype contained the component originally
            if (entityPrototype?.Components.TryGetComponent(name, out var entry) ?? false)
            {
                var comp = (Component)_componentFactory.GetComponent(name);
                var temp = (object)comp;
                _serialization.CopyTo(entry, ref temp);
                EntityManager.RemoveComponent(uid, temp!.GetType());
                EntityManager.AddComponent(uid, (Component)temp);
                continue;
            }

            EntityManager.RemoveComponentDeferred(uid, _componentFactory.GetRegistration(name).Type);
        }
        component.CurrentNodeId = null;
    }

    [PublicAPI]
    public ArtifactNode GetNodeFromId(int id, ArtifactComponent component)
    {
        return component.NodeTree.First(x => x.Id == id);
    }

    [PublicAPI]
    public ArtifactNode GetNodeFromId(int id, IEnumerable<ArtifactNode> nodes)
    {
        return nodes.First(x => x.Id == id);
    }
}