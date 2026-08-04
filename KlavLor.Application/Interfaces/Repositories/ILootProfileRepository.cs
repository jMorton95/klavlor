using KlavLor.Application.Features.Loot.Log;

namespace KlavLor.Application.Interfaces.Repositories;

// The character profile's own aggregates, plus bulk deletion of a character's or user's records.
// One of the five repositories ILootLogRepository was split into, grouped by consumer feature.
public interface ILootProfileRepository
{
    Task<ProfileHeader?> GetProfileHeader(int characterId);
    Task<WindowStats> GetWindowStats(int characterId, DateTimeOffset? from, DateTimeOffset? to);
    Task<List<DayBucket>> GetActivityCalendar(int characterId, DateTimeOffset from, DateTimeOffset to);
    Task<MonthlyTrend> GetMonthlyTrend(int characterId, DateTimeOffset? from, DateTimeOffset to, string range);
    Task<MonthlyRollTrend> GetMonthlyRolls(int characterId, DateTimeOffset? from, DateTimeOffset to, string range);
    Task<PersonalRecords> GetPersonalRecords(int characterId);
    Task<TopItemsList> GetTopItems(int characterId, int limit);

    Task DeleteAllForCharacter(int characterId);
    Task DeleteAllForUser(int userId);
}
