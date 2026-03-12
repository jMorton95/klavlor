using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface ITemplateRepository
{
    Task<Template?> GetById(int id);
    Task<bool> SaveTemplate(Template template);
    Task<int> DeleteTemplate(int id);
    Task<int?> GetTemplateOwnerId(int templateId);
    Task<bool> UpdateNodePosition(int nodeId, double positionX, double positionY);
    Task<bool> UpdateGroupPosition(int groupId, double positionX, double positionY);
    Task<bool> NodeBelongsToTemplate(int nodeId, int templateId);
}
