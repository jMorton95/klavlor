using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace KlavLor.Domain.Entities;

public sealed class Template : Entity
{
    public Template(string name, string? description, int createdById)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Template name cannot be empty.");

        Name = name;
        Description = description;
        CreatedById = createdById;
    }

    [Required, StringLength(100)]
    public string Name { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Required]
    public int CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public User? CreatedBy { get; set; }

    [Required]
    public bool IsPublic { get; set; }

    private readonly List<TemplateNode> _nodes = [];
    public IReadOnlyCollection<TemplateNode> Nodes => _nodes.AsReadOnly();

    private readonly List<TemplateEdge> _edges = [];
    public IReadOnlyCollection<TemplateEdge> Edges => _edges.AsReadOnly();

    private readonly List<TemplateNodeGroup> _groups = [];
    public IReadOnlyCollection<TemplateNodeGroup> Groups => _groups.AsReadOnly();

    private readonly List<LayoutSnapshot> _layoutSnapshots = [];
    public IReadOnlyCollection<LayoutSnapshot> LayoutSnapshots => _layoutSnapshots.AsReadOnly();

    private readonly List<CanvasAnnotation> _annotations = [];
    public IReadOnlyCollection<CanvasAnnotation> Annotations => _annotations.AsReadOnly();

    private readonly List<CanvasRegion> _regions = [];
    public IReadOnlyCollection<CanvasRegion> Regions => _regions.AsReadOnly();

    public void UpdateDetails(string name, string? description, bool isPublic)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Template name cannot be empty.");
        Name = name;
        Description = description;
        IsPublic = isPublic;
    }

    public TemplateNode AddNode(string label, NodeType nodeType, double positionX, double positionY, int? gearItemId = null, string? metadata = null, string? iconUrl = null, int? groupId = null, string? color = null)
    {
        ValidatePosition(positionX, positionY);

        if (groupId is not null && _groups.All(g => g.Id != groupId))
            throw new DomainException("Group not found.");

        var sortOrder = _nodes.Where(n => n.GroupId == groupId).Select(n => n.SortOrder).DefaultIfEmpty(-1).Max() + 1;

        var node = new TemplateNode
        {
            TemplateId = Id,
            Label = label,
            NodeType = nodeType,
            PositionX = positionX,
            PositionY = positionY,
            GearItemId = gearItemId,
            Metadata = metadata,
            IconUrl = iconUrl,
            GroupId = groupId,
            SortOrder = sortOrder,
            Color = color ?? "amber"
        };
        _nodes.Add(node);
        return node;
    }

    public (TemplateNodeGroup group, TemplateNode node) AddNodeToNewGroup(string label, NodeType nodeType, double positionX, double positionY, int? gearItemId = null, string? metadata = null, string? iconUrl = null, string? color = null)
    {
        ValidatePosition(positionX, positionY);

        var group = new TemplateNodeGroup
        {
            TemplateId = Id,
            PositionX = positionX,
            PositionY = positionY
        };
        _groups.Add(group);

        var node = new TemplateNode
        {
            TemplateId = Id,
            Label = label,
            NodeType = nodeType,
            PositionX = positionX,
            PositionY = positionY,
            GearItemId = gearItemId,
            Metadata = metadata,
            IconUrl = iconUrl,
            Group = group,
            SortOrder = 0,
            Color = color ?? "amber"
        };
        _nodes.Add(node);

        return (group, node);
    }

    public void MoveNodeUp(int nodeId)
    {
        var node = _nodes.SingleOrDefault(n => n.Id == nodeId);
        if (node == null) throw new DomainException("Node not found.");

        var groupNodes = _nodes.Where(n => n.GroupId == node.GroupId).OrderBy(n => n.SortOrder).ThenBy(n => n.Id).ToList();
        var idx = groupNodes.IndexOf(node);
        if (idx <= 0) return;

        var prev = groupNodes[idx - 1];
        (node.SortOrder, prev.SortOrder) = (prev.SortOrder, node.SortOrder);
    }

    public void MoveNodeDown(int nodeId)
    {
        var node = _nodes.SingleOrDefault(n => n.Id == nodeId);
        if (node == null) throw new DomainException("Node not found.");

        var groupNodes = _nodes.Where(n => n.GroupId == node.GroupId).OrderBy(n => n.SortOrder).ThenBy(n => n.Id).ToList();
        var idx = groupNodes.IndexOf(node);
        if (idx < 0 || idx >= groupNodes.Count - 1) return;

        var next = groupNodes[idx + 1];
        (node.SortOrder, next.SortOrder) = (next.SortOrder, node.SortOrder);
    }

    public TemplateEdge AddEdge(int fromNodeId, int toNodeId)
    {
        if (fromNodeId == toNodeId)
            throw new DomainException("Cannot create an edge from a node to itself.");

        if (_edges.Any(e => e.FromNodeId == fromNodeId && e.ToNodeId == toNodeId))
            throw new DomainException("This edge already exists.");

        var edge = new TemplateEdge
        {
            TemplateId = Id,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId
        };
        _edges.Add(edge);
        return edge;
    }

    public void RemoveNode(int nodeId)
    {
        var node = _nodes.SingleOrDefault(n => n.Id == nodeId);
        if (node == null) throw new DomainException("Node not found.");

        _edges.RemoveAll(e => e.FromNodeId == nodeId || e.ToNodeId == nodeId);
        _nodes.Remove(node);
    }

    public void RemoveEdge(int edgeId)
    {
        var edge = _edges.SingleOrDefault(e => e.Id == edgeId);
        if (edge == null) throw new DomainException("Edge not found.");
        _edges.Remove(edge);
    }

    public TemplateNodeGroup AddGroup(double positionX, double positionY)
    {
        ValidatePosition(positionX, positionY);

        var group = new TemplateNodeGroup
        {
            TemplateId = Id,
            PositionX = positionX,
            PositionY = positionY
        };
        _groups.Add(group);
        return group;
    }

    public void RemoveGroup(int groupId)
    {
        var group = _groups.SingleOrDefault(g => g.Id == groupId);
        if (group == null) throw new DomainException("Group not found.");

        var groupNodes = _nodes.Where(n => n.GroupId == groupId).ToList();
        foreach (var node in groupNodes)
        {
            _edges.RemoveAll(e => e.FromNodeId == node.Id || e.ToNodeId == node.Id);
            _nodes.Remove(node);
        }

        _groups.Remove(group);
    }

    public void AssignNodeToGroup(int nodeId, int? groupId)
    {
        var node = _nodes.SingleOrDefault(n => n.Id == nodeId);
        if (node == null) throw new DomainException("Node not found.");

        if (groupId is not null && _groups.All(g => g.Id != groupId))
            throw new DomainException("Group not found.");

        node.GroupId = groupId;
    }

    // --- Layout Snapshots ---

    public LayoutSnapshot CreateLayoutSnapshot(string positionDataJson)
    {
        const int maxSnapshots = 10;
        while (_layoutSnapshots.Count >= maxSnapshots)
        {
            var oldest = _layoutSnapshots.OrderBy(s => s.SavedAt).First();
            _layoutSnapshots.Remove(oldest);
        }

        var snapshot = new LayoutSnapshot
        {
            TemplateId = Id,
            PositionData = positionDataJson
        };
        _layoutSnapshots.Add(snapshot);
        return snapshot;
    }

    public LayoutSnapshot? GetLatestLayoutSnapshot()
    {
        return _layoutSnapshots.OrderByDescending(s => s.SavedAt).FirstOrDefault();
    }

    public void RemoveLayoutSnapshot(int snapshotId)
    {
        var snapshot = _layoutSnapshots.SingleOrDefault(s => s.Id == snapshotId);
        if (snapshot == null) throw new DomainException("Layout snapshot not found.");
        _layoutSnapshots.Remove(snapshot);
    }

    public void ApplyAutoLayout()
    {
        if (_groups.Count == 0) return;

        // Build group-to-group DAG from node edges
        var groupIds = _groups.Select(g => g.Id).ToHashSet();
        var adjacency = new Dictionary<int, List<int>>();
        var reverseAdj = new Dictionary<int, List<int>>();
        var inDegree = new Dictionary<int, int>();
        foreach (var gid in groupIds)
        {
            adjacency[gid] = [];
            reverseAdj[gid] = [];
            inDegree[gid] = 0;
        }

        var processedPairs = new HashSet<(int, int)>();
        foreach (var edge in _edges)
        {
            var fromNode = _nodes.FirstOrDefault(n => n.Id == edge.FromNodeId);
            var toNode = _nodes.FirstOrDefault(n => n.Id == edge.ToNodeId);
            if (fromNode?.GroupId == null || toNode?.GroupId == null) continue;
            if (fromNode.GroupId == toNode.GroupId) continue;

            var pair = (fromNode.GroupId.Value, toNode.GroupId.Value);
            if (!processedPairs.Add(pair)) continue;

            if (adjacency.ContainsKey(pair.Item1) && adjacency.ContainsKey(pair.Item2))
            {
                adjacency[pair.Item1].Add(pair.Item2);
                reverseAdj[pair.Item2].Add(pair.Item1);
                inDegree[pair.Item2]++;
            }
        }

        // Assign each group to its longest-path layer (ensures convergence nodes
        // appear only after ALL predecessors, not just the shortest path)
        var longestPath = new Dictionary<int, int>();
        foreach (var gid in groupIds) longestPath[gid] = 0;

        var topoOrder = new List<int>();
        var queue = new Queue<int>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var tempInDegree = new Dictionary<int, int>(inDegree);
        var visited = new HashSet<int>();

        while (queue.Count > 0)
        {
            var gid = queue.Dequeue();
            if (!visited.Add(gid)) continue;
            topoOrder.Add(gid);

            foreach (var neighbor in adjacency[gid])
            {
                longestPath[neighbor] = Math.Max(longestPath[neighbor], longestPath[gid] + 1);
                tempInDegree[neighbor]--;
                if (tempInDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        // Build layers from longest-path assignments
        var maxLayer = visited.Count > 0 ? visited.Max(g => longestPath[g]) : 0;
        var layers = new List<List<int>>();
        for (var i = 0; i <= maxLayer; i++)
            layers.Add([]);
        foreach (var gid in visited)
            layers[longestPath[gid]].Add(gid);

        // Disconnected groups (cycles or truly isolated) go in a final layer
        var disconnected = groupIds.Except(visited).ToList();
        if (disconnected.Count > 0)
            layers.Add(disconnected);

        // Pre-compute approximate height for each group based on node count
        const double groupItemHeight = 28;
        const double groupPadding = 24; // top + bottom padding
        const double groupAddBtnHeight = 24;
        const double groupMinHeight = 76;
        const double verticalGap = 40; // gap between groups

        var nodeCountByGroup = _nodes
            .Where(n => n.GroupId.HasValue)
            .GroupBy(n => n.GroupId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        double GetGroupHeight(int gid)
        {
            var count = nodeCountByGroup.GetValueOrDefault(gid, 1);
            return Math.Max(groupMinHeight, count * groupItemHeight + groupPadding + groupAddBtnHeight);
        }

        // Order groups within each layer by the average Y of their predecessors.
        // This keeps connected paths vertically coherent instead of interleaving
        // independent branches.
        var groupY = new Dictionary<int, double>();
        const double layerSpacing = 280;

        for (var layerIdx = 0; layerIdx < layers.Count; layerIdx++)
        {
            var layer = layers[layerIdx];

            if (layerIdx > 0 && layer.Count > 1)
            {
                // Sort by average predecessor Y center position
                layer.Sort((a, b) =>
                {
                    var aParents = reverseAdj.GetValueOrDefault(a, []);
                    var bParents = reverseAdj.GetValueOrDefault(b, []);
                    var aAvg = aParents.Count > 0 ? aParents.Average(p => groupY.GetValueOrDefault(p, 0)) : 0;
                    var bAvg = bParents.Count > 0 ? bParents.Average(p => groupY.GetValueOrDefault(p, 0)) : 0;
                    return aAvg.CompareTo(bAvg);
                });
            }

            // Compute total column height using actual group sizes
            var heights = layer.Select(GetGroupHeight).ToList();
            var totalHeight = heights.Sum() + (layer.Count - 1) * verticalGap;
            var currentY = Math.Max(50, 200 - totalHeight / 2);

            for (var i = 0; i < layer.Count; i++)
            {
                var centerY = currentY + heights[i] / 2;
                groupY[layer[i]] = centerY;

                var group = _groups.SingleOrDefault(g => g.Id == layer[i]);
                if (group == null) continue;
                group.PositionX = 100 + layerIdx * layerSpacing;
                group.PositionY = currentY;

                currentY += heights[i] + verticalGap;
            }
        }
    }

    // --- Annotations ---

    public CanvasAnnotation AddAnnotation(string text, double positionX, double positionY, string fontSize)
    {
        ValidatePosition(positionX, positionY);
        if (_annotations.Count >= 100)
            throw new DomainException("Maximum of 100 annotations per template.");

        var annotation = new CanvasAnnotation
        {
            TemplateId = Id,
            Text = text,
            PositionX = positionX,
            PositionY = positionY,
            FontSize = fontSize
        };
        _annotations.Add(annotation);
        return annotation;
    }

    public void UpdateAnnotation(int annotationId, string text, string fontSize)
    {
        var annotation = _annotations.SingleOrDefault(a => a.Id == annotationId);
        if (annotation == null) throw new DomainException("Annotation not found.");
        annotation.Text = text;
        annotation.FontSize = fontSize;
    }

    public void RemoveAnnotation(int annotationId)
    {
        var annotation = _annotations.SingleOrDefault(a => a.Id == annotationId);
        if (annotation == null) throw new DomainException("Annotation not found.");
        _annotations.Remove(annotation);
    }

    // --- Regions ---

    public CanvasRegion AddRegion(double positionX, double positionY, double width, double height, string color, double opacity, string? label)
    {
        ValidatePosition(positionX, positionY);
        if (_regions.Count >= 50)
            throw new DomainException("Maximum of 50 regions per template.");

        width = Math.Clamp(width, 50, 10000);
        height = Math.Clamp(height, 50, 10000);
        opacity = Math.Clamp(opacity, 0.05, 0.5);

        var region = new CanvasRegion
        {
            TemplateId = Id,
            Label = label,
            PositionX = positionX,
            PositionY = positionY,
            Width = width,
            Height = height,
            Color = color,
            Opacity = opacity
        };
        _regions.Add(region);
        return region;
    }

    public void UpdateRegion(int regionId, string? label, string color, double opacity)
    {
        var region = _regions.SingleOrDefault(r => r.Id == regionId);
        if (region == null) throw new DomainException("Region not found.");
        region.Label = label;
        region.Color = color;
        region.Opacity = Math.Clamp(opacity, 0.05, 0.5);
    }

    public void RemoveRegion(int regionId)
    {
        var region = _regions.SingleOrDefault(r => r.Id == regionId);
        if (region == null) throw new DomainException("Region not found.");
        _regions.Remove(region);
    }

    private static void ValidatePosition(double x, double y)
    {
        if (double.IsNaN(x) || double.IsInfinity(x) || x < -10000 || x > 100000)
            throw new DomainException("Position X is out of valid range.");
        if (double.IsNaN(y) || double.IsInfinity(y) || y < -10000 || y > 100000)
            throw new DomainException("Position Y is out of valid range.");
    }

}
