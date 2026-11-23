using System;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;

namespace TaskTracker.Core.Extensions
{
    public class ValidationErrorDetails:ErrorDetails
    {
        public IReadOnlyList<ValidationFieldError> Errors { get; init; } = [];
    }

    public sealed record ValidationFieldError(string PropertyName, string ErrorMessage, string ErrorCode)
    {
        public const string Summary = "The request contains invalid fields.";
        private static readonly Regex FieldPath = new(@"\A[A-Za-z_]\w*(?:\.\w+|\[\d+\])*\z",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

        public static ValidationFieldError FromFailure(ValidationFailure failure)
        {
            // Field paths come from rule definitions. Messages, codes and metadata can contain input.
            var field = failure.PropertyName is { Length: > 0 and <= 128 } path && FieldPath.IsMatch(path)
                ? path : "Request";
            var (code, message) = (failure.ErrorCode, field) switch
            {
                ("EqualValidator" or "fields_do_not_match", "ConfirmNewPassword") =>
                    ("fields_do_not_match", "The passwords must match."),
                ("MustValidator" or "password_length", "Password" or "NewPassword") =>
                    ("password_length", "Password must be between 12 and 128 characters."),
                ("MustValidator", "CurrentPassword") => ("required", "Current password is required."),
                ("MustValidator" or "invalid_email" or "EmailValidator" or "AspNetCoreCompatibleEmailValidator", "Email") =>
                    ("invalid_email", "A valid email address up to 200 characters is required."),
                ("MustValidator" or "invalid_username", "UserName") =>
                    ("invalid_username", "Username must contain 1 to 50 characters and no control characters."),
                ("NotNullValidator" or "NotEmptyValidator" or "required", _) =>
                    ("required", "This field is required."),
                ("MaximumLengthValidator" or "max_length", _) =>
                    ("max_length", "This field exceeds the maximum length."),
                ("MinimumLengthValidator" or "min_length", _) =>
                    ("min_length", "This field is too short."),
                (_, _) => ("invalid_value", "This field has an invalid value.")
            };
            return new ValidationFieldError(field, message, code);
        }

        public static ValidationException CreateException(IEnumerable<ValidationFailure> errors) =>
            new(Summary, errors.Select(FromFailure).Select(error =>
                new ValidationFailure(error.PropertyName, error.ErrorMessage) { ErrorCode = error.ErrorCode }).ToArray());
    }
}
