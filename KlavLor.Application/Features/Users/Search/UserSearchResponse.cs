namespace KlavLor.Application.Features.Users.Search;

public sealed record UserSearchResponse(
    int Id, string FirstName, string LastName, string Email, bool IsActive, bool IsLockedOut, string[] RoleNames
)
{
    public string FullName => $"{FirstName} {LastName}";
}
