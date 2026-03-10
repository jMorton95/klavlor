using System.Text.Json;
using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Import;

public sealed class ImportTemplateHandler(
    ITemplateRepository templateRepository,
    ImportTemplateValidator validator)
{
    public async Task<Result<Template>> Handle(ImportTemplateCommand command, int userId)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result<Template>.ValidationFailure(validationResult.ToDictionary());

        string[][]? groups;
        try
        {
            groups = JsonSerializer.Deserialize<string[][]>(command.JsonData);
        }
        catch
        {
            return Result<Template>.Failure("Invalid import format. Expected a JSON array of arrays.");
        }

        if (groups is null || groups.Length == 0)
            return Result<Template>.Failure("Import data is empty.");

        var template = new Template(command.Name, command.Description, userId);

        // Add nodes for each group, track by group
        var nodesByGroup = new List<List<TemplateNode>>();
        for (var g = 0; g < groups.Length; g++)
        {
            var groupNodes = new List<TemplateNode>();
            for (var i = 0; i < groups[g].Length; i++)
            {
                var label = groups[g][i].Trim();
                if (string.IsNullOrEmpty(label)) continue;
                var posX = 100.0 + g * 220.0;
                var posY = 100.0 + i * 80.0;
                var node = template.AddNode(label, NodeType.Item, posX, posY);
                groupNodes.Add(node);
            }
            nodesByGroup.Add(groupNodes);
        }

        // Save to get node IDs assigned by EF Core
        await templateRepository.SaveTemplate(template);

        // Add edges between consecutive groups
        for (var g = 0; g < nodesByGroup.Count - 1; g++)
        {
            foreach (var fromNode in nodesByGroup[g])
            {
                foreach (var toNode in nodesByGroup[g + 1])
                {
                    template.AddEdge(fromNode.Id, toNode.Id);
                }
            }
        }

        // Save again with edges
        await templateRepository.SaveTemplate(template);

        return Result<Template>.Success(template);
    }
}
