using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>Versioned, replayable conveyor authority. Inventories hold all cargo, including wreck cargo.</summary>
public readonly record struct ConveyorFlowState(InventoryId Buffer, double Capacity,
    int InputCursor = 0, int OutputCursor = 0, int ResourceCursor = 0,
    double InputPerSecond = 0, double OutputPerSecond = 0, double ActiveSeconds = 0,
    ConveyorLinkStatus Status = ConveyorLinkStatus.NoInput, string Cause = "Awaiting simulation",
    EntityId Network = default, EntityId InputA = default, EntityId InputB = default,
    EntityId OutputA = default, EntityId OutputB = default,
    double OutputAPerSecond = 0, double OutputBPerSecond = 0);

public readonly record struct ConveyorPortArbitration(int ResourceCursor);

/// <summary>Only explicit, directed, height-compatible sockets participate. No road edges or vehicle entities.</summary>
public sealed class ConveyorSimulationSystem : ISimulationSystem
{
    private readonly Dictionary<EntityId, ConveyorFlowObservation> _observations = [];
    public SimulationTick ObservationTick { get; private set; }
    public bool TryGetObservation(EntityId entity, out ConveyorFlowObservation observation) =>
        _observations.TryGetValue(entity, out observation);

    public const double NominalThroughput = 100;
    // Transfer every positive remainder; never discard fractional cargo to an epsilon.
    private const double Epsilon = 0;
    private readonly InventoryStore _stock;
    private readonly AutomatedDistributionSystem _distribution;
    private readonly ProductionRecipeCatalog _recipes;
    private readonly UnitDefinitionCatalog _units;
    public ConveyorSimulationSystem(InventoryStore stock, AutomatedDistributionSystem distribution,
        ProductionRecipeCatalog recipes, UnitDefinitionCatalog units)
    { _stock = stock; _distribution = distribution; _recipes = recipes; _units = units; }
    public SimulationPhase Phase => SimulationPhase.Economy;
    public static IReadOnlyList<ResourceId> EligibleResources { get; } = Array.AsReadOnly(new[]
    { ResourceIds.FerrousOre, ResourceIds.Silicates, ResourceIds.Steel, ResourceIds.Electronics, ResourceIds.Ammunition });
    public static bool Eligible(ResourceId resource) => EligibleResources.Contains(resource);
    public static double Capacity(ConveyorModule module) => module is ConveyorModule.ShortSegment or
        ConveyorModule.InputAdapter or ConveyorModule.OutputAdapter or ConveyorModule.Merger or ConveyorModule.Splitter ? 100 : 200;

    public void Execute(SimulationContext context)
    {
        _observations.Clear();
        ObservationTick = context.Tick;
        var entities = context.Entities;
        double seconds = context.TickDuration.TotalSeconds;
        if (seconds <= 0 || !double.IsFinite(seconds)) return;
        var nodes = new List<Node>();
        var owners = new HashSet<PlayerId>();
        var brokenFeet = new Dictionary<(PlayerId, int, int), EntityId>();
        foreach (EntityId foot in entities.Query<ConveyorPiece, HealthState>(QueryIterationOrder.StableByEntityIndex))
        {
            var support = entities.GetComponent<ConveyorPiece>(foot);
            if (support.Placement.IsSupport && entities.GetComponent<HealthState>(foot).IsDepleted)
                brokenFeet.TryAdd((support.Owner, (int)MathF.Round(support.Placement.Pivot.X*10), (int)MathF.Round(support.Placement.Pivot.Z*10)), foot);
        }
        foreach (EntityId e in entities.Query<ConveyorPiece>(QueryIterationOrder.StableByEntityIndex))
        {
            var piece = entities.GetComponent<ConveyorPiece>(e);
            owners.Add(piece.Owner);
            if (piece.Placement.IsSupport || piece.Placement.Module == ConveyorModule.Endcap) continue;
            if (!entities.TryGetComponent(e, out ConveyorFlowState flow) || !_stock.Contains(flow.Buffer)) continue;
            double scale = OperationalScale(entities, e);
            ConveyorSocket[] sockets = piece.Placement.Sockets;
            // A broken supporting foot stops its attached spans, without projecting the span onto the road.
            EntityId brokenSupport = default;
            if (brokenFeet.Count != 0)
                foreach (ConveyorSocket socket in sockets)
                    if (brokenFeet.TryGetValue((piece.Owner, (int)MathF.Round(socket.Position.X * 10),
                        (int)MathF.Round(socket.Position.Z * 10)), out brokenSupport)) { scale = 0; break; }
            nodes.Add(new(e, piece.Owner, flow.Buffer, sockets, flow,
                NominalThroughput * seconds * scale, piece.Attachment) { BrokenSupport = brokenSupport });
        }
        if (nodes.Count == 0) return;
        foreach (PlayerId owner in owners.OrderBy(o => o.Value))
            foreach (var port in ConveyorConstructionService.CapturePorts(entities, owner))
            {
                InventoryId inventory = ResolveInventory(entities, port.Building, port.Input);
                if (!_stock.Contains(inventory)) continue;
                int cursor = entities.TryGetComponent(port.Building, out ConveyorPortArbitration arbitration) ? arbitration.ResourceCursor : 0;
                var node=new Node(port.Building, owner, inventory, [new(port.Position, port.Facing, port.Input)],
                    new(default, 0, ResourceCursor: cursor), NominalThroughput * seconds * OperationalScale(entities, port.Building),
                    default, port);
                node.AllowedResources = AcceptedResources(entities, node);
                nodes.Add(node);
            }
        Connect(nodes);
        // Admit building cargo only when an authored, resource-compatible sink is reachable.
        // Full/offline sinks still propagate backpressure through the finite physical buffers.
        for (int r = 0; r < EligibleResources.Count; r++)
        {
            var queue = new Queue<Node>();
            foreach (var node in nodes.Where(n => n.Port is { Input: true }))
                if ((node.AllowedResources & (1 << r))!=0) { node.AcceptedDownstream |= 1 << r; queue.Enqueue(node); }
            while (queue.TryDequeue(out var sink))
                foreach (var link in sink.Inputs)
                    if ((link.Source.AcceptedDownstream & (1 << r)) == 0)
                    { link.Source.AcceptedDownstream |= 1 << r; queue.Enqueue(link.Source); }
        }
        // Snapshots are shared per inventory: newly received cargo cannot depart in this tick.
        var available = new Dictionary<InventoryId, double[]>();
        foreach (var node in nodes)
            if (!available.ContainsKey(node.Inventory))
            {
                var quantities = new double[EligibleResources.Count];
                for (int r = 0; r < quantities.Length; r++)
                    quantities[r] = _stock.GetAvailableQuantity(node.Inventory, EligibleResources[r]);
                available[node.Inventory] = quantities;
            }
        // This index is rebuilt from authoritative policies every tick, including full entity generations.
        // Duplicate policies retain the former minimum input target and maximum retained output stock.
        var targets = new Dictionary<(EntityId, ResourceId), (double Minimum, double Maximum)>();
        foreach (EntityId e in entities.Query<LogisticsStockPolicy>(QueryIterationOrder.StableByEntityIndex))
        {
            var policy = entities.GetComponent<LogisticsStockPolicy>(e);
            if (!policy.Enabled) continue;
            var key = (policy.TargetEntity, policy.ResourceId);
            targets[key] = targets.TryGetValue(key, out var target)
                ? (Math.Min(target.Minimum, policy.DesiredTarget), Math.Max(target.Maximum, policy.DesiredTarget))
                : (policy.DesiredTarget, policy.DesiredTarget);
        }
        foreach (var node in nodes)
        {
            if (node.Port is not { Input: false } || ResolveInventory(entities, node.Entity, true) != node.Inventory) continue;
            for (int r = 0; r < EligibleResources.Count; r++)
            {
                double retain = targets.TryGetValue((node.Entity, EligibleResources[r]), out var target) ? target.Maximum : 0;
                available[node.Inventory][r] = Math.Max(0, available[node.Inventory][r] - retain);
            }
        }
        var limits=new Dictionary<(EntityId,ResourceId),double>();
        foreach(var node in nodes)
        {
            if (node.Port is not { Input: true }) continue;
            foreach(var resource in EligibleResources)
            {
                double target = targets.TryGetValue((node.Entity, resource), out var policy) ? policy.Minimum : double.PositiveInfinity;
                limits[(node.Entity,resource)]=target-_distribution.GetCommittedInbound(entities,node.Entity,resource);
            }
        }
        // Match one incoming offer per receiver and one output per sender each round.
        // Successful transfers alone advance cursors. Blocked branches are skipped; spare capacity is reused.
        // Two-port contention and each input resource's fractional tail need bounded extra rounds.
        int rounds = checked((int)Math.Min(100000,
            Math.Ceiling(NominalThroughput * seconds) * 2 + EligibleResources.Count * 2 + 2));
        for (int round = 0; round < rounds; round++)
        {
            var offers = new Dictionary<Node, List<Link>>();
            foreach (var receiver in nodes)
            {
                if (receiver.InRemaining <= Epsilon || receiver.Inputs.Count == 0) continue;
                for (int i = 0; i < receiver.Inputs.Count; i++)
                {
                    var link = receiver.Inputs[(receiver.InputCursor + i) % receiver.Inputs.Count];
                    if (FindResource(link) < 0) continue;
                    if (!offers.TryGetValue(link.Source, out var list)) offers[link.Source] = list = [];
                    list.Add(link);
                    break;
                }
            }
            bool progress = false;
            foreach (var sender in nodes)
            {
                if (!offers.TryGetValue(sender, out var candidates)) continue;
                for (int i = 0; i < sender.Outputs.Count; i++)
                {
                    var link = sender.Outputs[(sender.OutputCursor + i) % sender.Outputs.Count];
                    if (!candidates.Contains(link)) continue;
                    int resource = FindResource(link);
                    if (resource < 0) continue;
                    var receiver = link.Destination;
                    ResourceId id = EligibleResources[resource];
                    double quantum = Math.Min(sender.Quantum, receiver.Quantum);
                    double quantity = Math.Min(quantum, Math.Min(sender.OutRemaining, receiver.InRemaining));
                    quantity = Math.Min(quantity, Math.Min(available[sender.Inventory][resource], Room(receiver, id)));
                    quantity = Math.Min(quantity, _stock.GetAddableQuantity(receiver.Inventory, id, quantity));
                    if (quantity <= Epsilon || !_stock.Transfer(sender.Inventory, receiver.Inventory, id, quantity).Succeeded) continue;
                    link.Transferred += quantity;
                    available[sender.Inventory][resource] -= quantity;
                    sender.OutRemaining -= quantity; sender.Sent += quantity;
                    receiver.InRemaining -= quantity; receiver.Received += quantity;
                    sender.OutputCursor = (sender.Outputs.IndexOf(link) + 1) % sender.Outputs.Count;
                    receiver.InputCursor = (receiver.Inputs.IndexOf(link) + 1) % receiver.Inputs.Count;
                    sender.ResourceCursor = (resource + 1) % EligibleResources.Count;
                    progress = true;
                    break;
                }
            }
            if (!progress) break;
        }
        foreach (var node in nodes)
        {
            if (node.Port is not null)
            {
                if (node.Sent > Epsilon)
                {
                    var arbitration = new ConveyorPortArbitration(node.ResourceCursor);
                    if (entities.HasComponent<ConveyorPortArbitration>(node.Entity)) entities.SetComponent(node.Entity, arbitration);
                    else entities.AddComponent(node.Entity, arbitration);
                }
                continue;
            }
            var piece = entities.GetComponent<ConveyorPiece>(node.Entity);
            double buffered = _stock.GetResourceQuantities(node.Inventory).Sum(r=>r.Quantity);
            bool moving = node.Sent > Epsilon || node.Received > Epsilon;
            bool dead = entities.TryGetComponent(node.Entity, out HealthState health) && health.IsDepleted;
            bool missing = node.Inputs.Count == 0 || node.Outputs.Count == 0;
            var status = dead ? ConveyorLinkStatus.Destroyed : node.Budget <= Epsilon ? ConveyorLinkStatus.Offline :
                moving ? ConveyorLinkStatus.Flowing : buffered > Epsilon ? ConveyorLinkStatus.Congested :
                missing ? ConveyorLinkStatus.Disconnected : node.AcceptedDownstream==0?ConveyorLinkStatus.Blocked:ConveyorLinkStatus.NoInput;
            _observations[node.Entity] = Observe(node, dead, buffered, moving);
            string cause = dead ? "Wreck cargo retained; repair to resume" : node.Budget <= Epsilon ? "Power or supporting foot unavailable" :
                moving ? "Transporting" : missing ? "Unmated or closed port" : buffered > Epsilon ? "Downstream full, reserved, offline or rejecting resource" : node.AcceptedDownstream==0?"No resource-compatible downstream building port":"No unreserved eligible input";
            if (!dead && health.Maximum > 0 && health.Fraction < .7)
                status = health.Fraction < .25 ? ConveyorLinkStatus.Critical : ConveyorLinkStatus.Damaged;
            entities.SetComponent(node.Entity, piece with { BufferedQuantity = buffered });
            entities.SetComponent(node.Entity, node.Flow with
            {
                InputCursor = node.InputCursor, OutputCursor = node.OutputCursor, ResourceCursor = node.ResourceCursor,
                InputPerSecond = node.Received / seconds, OutputPerSecond = node.Sent / seconds,
                ActiveSeconds = node.Flow.ActiveSeconds + (moving ? seconds : 0), Status = status, Cause = cause,
                Network = node.Root.Entity,
                InputA = node.Inputs.Count > 0 ? node.Inputs[0].Source.Entity : default,
                InputB = node.Inputs.Count > 1 ? node.Inputs[1].Source.Entity : default,
                OutputA = node.Outputs.Count > 0 ? node.Outputs[0].Destination.Entity : default,
                OutputB = node.Outputs.Count > 1 ? node.Outputs[1].Destination.Entity : default,
                OutputAPerSecond = node.Outputs.Count > 0 ? node.Outputs[0].Transferred / seconds : 0,
                OutputBPerSecond = node.Outputs.Count > 1 ? node.Outputs[1].Transferred / seconds : 0
            });
        }

        ConveyorFlowObservation Observe(Node node, bool dead, double buffered, bool moving)
        {
            ConveyorBlockObservation first = default, second = default, third = default;
            if (dead) Add(new(ConveyorBlockReason.DestroyedRoute, node.Entity));
            else if (entities.TryGetComponent(node.Entity, out HealthState damage) && damage.Fraction < .7)
                Add(new(ConveyorBlockReason.DamagedRoute, node.Entity));
            if (node.BrokenSupport.IsValid) Add(new(ConveyorBlockReason.DestroyedSupport, node.BrokenSupport));
            if (!entities.TryGetComponent(node.Entity, out PowerConsumer drive) || drive.OperationalScale <= 0)
                Add(new(ConveyorBlockReason.NoPower, node.Entity));
            if (node.Inputs.Count == 0 || node.Outputs.Count == 0)
                Add(new(ConveyorBlockReason.DisconnectedPort, node.Entity));
            if (!moving && buffered > 0)
                foreach (var link in node.Outputs)
                {
                    var target = link.Destination;
                    if (entities.TryGetComponent(target.Entity, out HealthState health) && health.IsDepleted)
                        Add(new(ConveyorBlockReason.DestroyedRoute, target.Entity));
                    else if (target.BrokenSupport.IsValid)
                        Add(new(ConveyorBlockReason.DestroyedSupport, target.BrokenSupport));
                    else if (target.Budget <= 0) Add(new(ConveyorBlockReason.NoPower, target.Entity));
                    for (int r = 0; r < EligibleResources.Count; r++)
                    {
                        ResourceId resource = EligibleResources[r];
                        if (_stock.GetQuantity(node.Inventory, resource) <= 0) continue;
                        if ((target.AllowedResources & (1 << r)) == 0)
                            Add(new(ConveyorBlockReason.IncompatibleResource, target.Entity, resource));
                        else if (_stock.GetAddableQuantity(target.Inventory, resource, 1) <= 0)
                            Add(new(ConveyorBlockReason.DestinationFull, target.Entity, resource));
                        else if (Room(target, resource) <= 0)
                            Add(new(ConveyorBlockReason.DestinationTargetCovered, target.Entity, resource));
                    }
                }
            if (!moving && buffered <= 0)
                foreach (var link in node.Inputs)
                {
                    var source = link.Source;
                    if (entities.TryGetComponent(source.Entity, out HealthState sourceHealth) && sourceHealth.IsDepleted)
                        Add(new(ConveyorBlockReason.DestroyedRoute, source.Entity));
                    else if (source.BrokenSupport.IsValid)
                        Add(new(ConveyorBlockReason.DestroyedSupport, source.BrokenSupport));
                    else if (source.Budget <= 0)
                        Add(new(ConveyorBlockReason.NoPower, source.Entity));
                    bool hasStock = false;
                    for (int r = 0; r < EligibleResources.Count; r++)
                    {
                        ResourceId resource = EligibleResources[r];
                        if (_stock.GetQuantity(source.Inventory, resource) <= 0) continue;
                        hasStock = true;
                        if (_stock.GetAvailableQuantity(source.Inventory, resource) <= 0)
                            Add(new(ConveyorBlockReason.ReservedStock, source.Entity, resource));
                        else if (source.Port is { Input: false } &&
                            targets.TryGetValue((source.Entity, resource), out var retained) &&
                            _stock.GetAvailableQuantity(source.Inventory, resource) <= retained.Maximum)
                            Add(new(ConveyorBlockReason.RetainedStock, source.Entity, resource));
                    }
                    if (!hasStock && source.Sent <= 0)
                        Add(new(ConveyorBlockReason.NoInput, source.Entity));
                }
            if (!moving && first.Reason == ConveyorBlockReason.None)
                Add(new(ConveyorBlockReason.Unknown, default));
            return new(first, second, third);

            void Add(ConveyorBlockObservation fact)
            {
                if (fact == first || fact == second || fact == third) return;
                if (first.Reason == ConveyorBlockReason.None || Compare(fact, first) < 0)
                { third = second; second = first; first = fact; }
                else if (second.Reason == ConveyorBlockReason.None || Compare(fact, second) < 0)
                { third = second; second = fact; }
                else if (third.Reason == ConveyorBlockReason.None || Compare(fact, third) < 0) third = fact;
            }
            static int Compare(ConveyorBlockObservation a, ConveyorBlockObservation b)
            {
                int reason = a.Reason.CompareTo(b.Reason);
                if (reason != 0) return reason;
                int entity = a.Cause.CompareTo(b.Cause);
                return entity != 0 ? entity : a.Resource.Value.CompareTo(b.Resource.Value);
            }
        }

        int FindResource(Link link)
        {
            var source = link.Source; var destination = link.Destination;
            if (source.OutRemaining <= Epsilon || destination.InRemaining <= Epsilon || source.Inventory == destination.Inventory) return -1;
            for (int i = 0; i < EligibleResources.Count; i++)
            {
                int r = (source.ResourceCursor + i) % EligibleResources.Count;
                ResourceId id = EligibleResources[r];
                if ((destination.AcceptedDownstream & (1 << r)) == 0) continue;
                if (available[source.Inventory][r] <= Epsilon || ((source.AllowedResources & destination.AllowedResources) & (1 << r)) == 0) continue;
                if (Room(destination, id) > Epsilon && _stock.GetAddableQuantity(destination.Inventory, id, 1) > Epsilon) return r;
            }
            return -1;
        }
        double Room(Node node, ResourceId resource)
        {
            if (node.Port is not { Input: true }) return double.PositiveInfinity;
            return Math.Max(0, limits[(node.Entity,resource)] - _stock.GetQuantity(node.Inventory, resource));
        }
    }

    private static double OperationalScale(EntityRegistry entities, EntityId entity)
    {
        if (entities.TryGetComponent(entity, out HealthState health) && health.IsDepleted) return 0;
        if (!entities.TryGetComponent(entity, out PowerConsumer power)) return 0;
        double damage = health.Maximum > 0 && health.Fraction < .7 ? health.Fraction : 1;
        return power.OperationalScale * damage;
    }
    public static InventoryId ResolveInventory(EntityRegistry entities, EntityId entity, bool input)
    {
        if (!entities.IsAlive(entity)) return default;
        if (entities.TryGetComponent(entity, out ProductionFacility production)) return input ? production.InputInventory : production.OutputInventory;
        if (input && entities.TryGetComponent(entity, out UnitProductionFacility factory)) return factory.InputInventory;
        if (!input && entities.TryGetComponent(entity, out ResourceExtractor source) &&
            entities.TryGetComponent(source.OutputInventory, out InventoryStorage actual)) return actual.InventoryId;
        if (entities.TryGetComponent(entity, out SupplyDepot supply)) return supply.InventoryId;
        if (entities.TryGetComponent(entity, out InventoryStorage storage)) return storage.InventoryId;
        if (entities.TryGetComponent(entity, out LogisticsHub hub)) return hub.InventoryId;
        return default;
    }
    private int AcceptedResources(EntityRegistry entities, Node node)
    {
        if (node.Port is not { } port) return (1 << EligibleResources.Count) - 1;
        BuildingId id = entities.GetComponent<CompletedBuilding>(port.Building).BuildingId;
        if (id == BuildingIds.Extractor)
            return !port.Input && entities.TryGetComponent(port.Building, out ResourceExtractor extractor) && extractor.Enabled &&
                extractor.Owner == new FactionId(checked((uint)node.Owner.Value)) ? ResourceMask(extractor.ResourceId) : 0;
        if (id == BuildingIds.Smelter || id == BuildingIds.ElectronicsPlant || id == BuildingIds.AmmunitionPlant)
        {
            int mask = 0;
            if (entities.TryGetComponent(port.Building, out ProductionFacility facility))
                foreach (var recipe in _recipes.Definitions)
                    if (facility.Supports(recipe.RequiredCapability))
                        foreach (var ingredient in port.Input ? recipe.Inputs : recipe.Outputs)
                            mask |= ResourceMask(ingredient.ResourceId);
            return mask;
        }
        if (id == BuildingIds.SupplyDepot) return ResourceMask(ResourceIds.Ammunition);
        if (id == BuildingIds.Barracks || id == BuildingIds.VehicleFactory)
        {
            int mask = 0;
            if (port.Input && entities.TryGetComponent(port.Building, out UnitProductionFacility factory))
                foreach (var unit in _units.Definitions)
                    if (factory.Supports(unit.RequiredProductionCapability))
                        foreach (var cost in unit.Costs) mask |= ResourceMask(cost.ResourceId);
            return mask;
        }
        return id == BuildingIds.StorageDepot || id == BuildingIds.LogisticsHub || id == BuildingIds.CommandCore
            ? (1 << EligibleResources.Count) - 1 : 0;

        static int ResourceMask(ResourceId resource)
        {
            for (int r = 0; r < EligibleResources.Count; r++)
                if (EligibleResources[r] == resource) return 1 << r;
            return 0;
        }
    }

    private static void Connect(List<Node> nodes)
    {
        var inputs = new Dictionary<(PlayerId, int, int, int), List<(Node Node, ConveyorSocket Socket, int Slot)>>();
        foreach (var node in nodes) for(int slot=0;slot<node.Sockets.Length;slot++)
        {
            var socket=node.Sockets[slot];
            if(!socket.Input)continue;
            var key = Key(node.Owner, socket.Position);
            if (!inputs.TryGetValue(key, out var bucket)) inputs[key] = bucket = [];
            bucket.Add((node, socket, slot));
        }
        var proposals = new List<Link>();
        var counts = new Dictionary<(Node, int), int>();
        foreach (var source in nodes) foreach (var output in source.Sockets)
        {
            if (output.Input) continue;
            var key = Key(source.Owner, output.Position);
            int matches = 0, inputSlot = 0;
            Node? matched = null;
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                if (inputs.TryGetValue((key.Item1, key.Item2 + x, key.Item3 + y, key.Item4 + z), out var bucket))
                    foreach (var candidate in bucket)
                    {
                        var destination = candidate.Node;
                        if (source.Entity == destination.Entity || Vector3.DistanceSquared(output.Position, candidate.Socket.Position) >= .0004f ||
                            Vector3.Dot(output.Facing, candidate.Socket.Facing) > -.999f) continue;
                        if (source.Port is not null && (destination.Port is not null || destination.Attachment != source.Entity)) continue;
                        if (destination.Port is not null && source.Attachment != destination.Entity) continue;
                        matches++;
                        matched = destination;
                        inputSlot = candidate.Slot;
                    }
            // Ambiguous overlapping sockets fail closed instead of cloning or choosing an arbitrary connection.
            if (matches == 1 && matched is not null)
            {
                proposals.Add(new(source, matched, output.Position, inputSlot));
                var input = (matched, inputSlot);
                counts[input] = counts.TryGetValue(input, out int count) ? count + 1 : 1;
            }
        }
        foreach (var link in proposals)
        {
            if (counts[(link.Destination, link.InputSlot)] != 1) continue;
            link.Source.Outputs.Add(link); link.Destination.Inputs.Add(link);
            var a = link.Source.Root; var b = link.Destination.Root;
            if (a != b) { if (a.Entity < b.Entity) b.Parent = a; else a.Parent = b; }
        }
        static (PlayerId, int, int, int) Key(PlayerId owner, Vector3 p) =>
            (owner, (int)MathF.Round(p.X * 50), (int)MathF.Round(p.Y * 50), (int)MathF.Round(p.Z * 50));
    }
    private sealed class Node
    {
        public Node(EntityId entity, PlayerId owner, InventoryId inventory, ConveyorSocket[] sockets,
            ConveyorFlowState flow, double budget, EntityId attachment, ConveyorBuildingPort? port = null)
        {
            Entity = entity; Owner = owner; Inventory = inventory; Sockets = sockets; Flow = flow;
            Budget = InRemaining = OutRemaining = budget; Attachment = attachment; Port = port;
            // Equal quanta avoid assigning every fractional tick remainder to the same branch.
            Quantum = budget > 0 ? budget / Math.Ceiling(budget) : 0;
            InputCursor = flow.InputCursor; OutputCursor = flow.OutputCursor; ResourceCursor = flow.ResourceCursor;
            Parent = this;
        }
        public EntityId BrokenSupport { get; init; }
        public EntityId Entity { get; }
        public PlayerId Owner { get; }
        public InventoryId Inventory { get; }
        public ConveyorSocket[] Sockets { get; }
        public ConveyorFlowState Flow { get; }
        public EntityId Attachment { get; }
        public ConveyorBuildingPort? Port { get; }
        public double Budget { get; }
        public double Quantum { get; }
        public double InRemaining, OutRemaining, Sent, Received;
        public int InputCursor, OutputCursor, ResourceCursor, AcceptedDownstream;
        public int AllowedResources=(1 << EligibleResources.Count)-1;
        public List<Link> Inputs { get; } = [];
        public List<Link> Outputs { get; } = [];
        public Node Parent;
        public Node Root => Parent == this ? this : Parent = Parent.Root;
    }
    private sealed record Link(Node Source, Node Destination, Vector3 Position, int InputSlot)
    {
        public double Transferred { get; set; }
    }
}

