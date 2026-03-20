using FluentValidation;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.ApiKeys.Create;

public sealed class ApiKeyCreateValidator : AbstractValidator<ApiKeyCreateCommand>
{
    public ApiKeyCreateValidator(IUserRepository userRepository)
    {
        RuleFor(x => x.UserId).GreaterThan(0)
            .MustAsync(async (userId, ct) => await userRepository.GetById(userId) is not null)
            .WithMessage("User does not exist.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
