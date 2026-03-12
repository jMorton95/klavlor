using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

    private static void ValidatePosition(double x, double y)
    {
        if (double.IsNaN(x) || double.IsInfinity(x) || x < -10000 || x > 100000)
            throw new DomainException("Position X is out of valid range.");
        if (double.IsNaN(y) || double.IsInfinity(y) || y < -10000 || y > 100000)
            throw new DomainException("Position Y is out of valid range.");
    }

}
