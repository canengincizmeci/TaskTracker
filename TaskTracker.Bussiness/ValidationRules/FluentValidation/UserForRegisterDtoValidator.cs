using FluentValidation;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.ValidationRules.FluentValidation;

public sealed class UserForRegisterDtoValidator : AbstractValidator<UserForRegisterDto>
{
    public UserForRegisterDtoValidator()
    {
        RuleFor(x => x.Password)
            .Must(password => password is { Length: >= 12 and <= 128 })
            .WithMessage("Password must be between 12 and 128 characters.");
    }
}
