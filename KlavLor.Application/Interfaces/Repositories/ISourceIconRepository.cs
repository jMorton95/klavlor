using KlavLor.Domain.Entities;

namespace KlavLor.Application.Interfaces.Repositories;

public interface ISourceIconRepository
{
    Task<SourceIcon?> GetBySourceName(string sourceName);
    Task<List<string>> FindUncataloguedSources(int limit);
    Task<List<SourceIcon>> GetPendingIcons(int limit);
    Task Save(SourceIcon icon);
    Task SaveRange(List<SourceIcon> icons);
}
