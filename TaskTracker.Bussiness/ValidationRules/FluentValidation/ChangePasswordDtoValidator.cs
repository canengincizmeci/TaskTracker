using FluentValidation;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.ValidationRules.FluentValidation;

public sealed class ChangePasswordDtoValidator : AbstractValidator<ChangePasswordDto>
{
    public ChangePasswordDtoValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .Must(password => password is { Length: > 0 })
            .WithMessage("Current password is required.");
        RuleFor(x => x.NewPassword)
            .Must(password => password is { Length: >= 12 and <= 128 })
            .WithMessage("Password must be between 12 and 128 characters.");
        RuleFor(x => x.ConfirmNewPassword).Equal(x => x.NewPassword);
    }
}
