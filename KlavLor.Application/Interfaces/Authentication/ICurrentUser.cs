namespace KlavLor.Application.Interfaces.Authentication;

public interface ICurrentUser
{
    int? UserId { get; }
    bool IsAdmin { get; }
}
