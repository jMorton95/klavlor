namespace KlavLor.Application.Features.Users.Create;

public sealed class UserCreateCommand
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordOne { get; set; } = "";
    public string PasswordTwo { get; set; } = "";
}
