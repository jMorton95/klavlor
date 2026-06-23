namespace KlavLor.Application.Features.Login;

public sealed class LoginCommand
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
