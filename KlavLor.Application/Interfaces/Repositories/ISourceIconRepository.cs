using KlavLor.Application.Features.Maintenance;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Interfaces.Repositories;

public interface ISourceIconRepository
{
    Task<SourceIcon?> GetBySourceName(string sourceName);
    Task<List<string>> FindUncataloguedSources(int limit);
    Task<List<SourceIcon>> GetPendingIcons(int limit);
    Task<List<SourceIcon>> GetFailedIcons(int limit);
    Task ResetFailure(int id);
    Task<IconStats> GetStats();
    Task Save(SourceIcon icon);
    Task SaveRange(List<SourceIcon> icons);
}
