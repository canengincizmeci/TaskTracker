using TaskTracker.Bussiness.ValidationRules.FluentValidation;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

public class PasswordValidationTests
{
    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void RegistrationEnforcesPasswordBounds(int length, bool expected)
    {
        var result = new UserForRegisterDtoValidator().Validate(new UserForRegisterDto
        {
            Email = "valid@example.test",
            UserName = "valid-user",
            Password = new string('x', length)
        });

        Assert.Equal(expected, result.IsValid);
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void ResetEnforcesPasswordBounds(int length, bool expected)
    {
        var password = new string('x', length);
        var result = new ResetPasswordDtoValidator().Validate(new ResetPasswordDto
        {
            ResetToken = "token",
            NewPassword = password,
            ConfirmNewPassword = password
        });

        Assert.Equal(expected, result.IsValid);
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void ChangeEnforcesPasswordBounds(int length, bool expected)
    {
        var password = new string('x', length);
        var result = new ChangePasswordDtoValidator().Validate(new ChangePasswordDto
        {
            CurrentPassword = "current",
            NewPassword = password,
            ConfirmNewPassword = password
        });

        Assert.Equal(expected, result.IsValid);
    }

    [Fact]
    public void PasswordsAreNotTrimmedOrCaseFolded()
    {
        var spaced = "            ";
        Assert.True(new UserForRegisterDtoValidator().Validate(new UserForRegisterDto
        {
            Email = "valid@example.test",
            UserName = "valid-user",
            Password = spaced
        }).IsValid);
        Assert.False(new ResetPasswordDtoValidator().Validate(new ResetPasswordDto
        {
            NewPassword = "CaseSensitive1",
            ConfirmNewPassword = "casesensitive1"
        }).IsValid);
    }

    [Theory]
    [InlineData("  Mixed.Case@Example.Test  ", "Display Name", true)]
    [InlineData("invalid-email", "Display Name", false)]
    [InlineData("valid@example.test", "   ", false)]
    [InlineData("valid@example.test", "name\u0000", false)]
    public void RegistrationValidatesCanonicalizableIdentityFields(
        string email,
        string userName,
        bool expected)
    {
        var result = new UserForRegisterDtoValidator().Validate(new UserForRegisterDto
        {
            Email = email,
            UserName = userName,
            Password = new string('x', 12)
        });

        Assert.Equal(expected, result.IsValid);
    }
}
