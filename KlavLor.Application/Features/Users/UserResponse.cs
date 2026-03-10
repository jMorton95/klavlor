namespace KlavLor.Application.Features.Users;

public sealed record UserResponse(int Id, string FirstName, string LastName, string Email, bool IsActive, string[] RoleNames)
{
    public string FullName => $"{FirstName} {LastName}";
}
