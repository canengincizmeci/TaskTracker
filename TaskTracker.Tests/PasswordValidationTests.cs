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
            Password = spaced
        }).IsValid);
        Assert.False(new ResetPasswordDtoValidator().Validate(new ResetPasswordDto
        {
            NewPassword = "CaseSensitive1",
            ConfirmNewPassword = "casesensitive1"
        }).IsValid);
    }
}
