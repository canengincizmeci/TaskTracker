using FluentValidation;
using System.ComponentModel.DataAnnotations;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.ValidationRules.FluentValidation;

public sealed class UserForRegisterDtoValidator : AbstractValidator<UserForRegisterDto>
{
    private static readonly IdentityNormalizer Normalizer = new();

    public UserForRegisterDtoValidator()
    {
        RuleFor(x => x.Email)
            .Must(BeValidEmail)
            .WithMessage("A valid email address up to 200 characters is required.");
        RuleFor(x => x.UserName)
            .Must(BeValidUserName)
            .WithMessage("Username must contain 1 to 50 characters and no control characters.");
        RuleFor(x => x.Password)
            .Must(password => password is { Length: >= 12 and <= 128 })
            .WithMessage("Password must be between 12 and 128 characters.");
    }

    private static bool BeValidEmail(string? value)
    {
        var canonical = value is null ? null : Normalizer.NormalizeEmail(value);
        return canonical is { Length: > 0 and <= 200 } && new EmailAddressAttribute().IsValid(canonical);
    }

    private static bool BeValidUserName(string? value)
    {
        var display = value is null ? null : Normalizer.TrimUserName(value);
        return display is { Length: > 0 and <= 50 } && display.All(character => !char.IsControl(character));
    }
}
