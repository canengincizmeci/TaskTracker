using FluentValidation;
using TaskTracker.Core.Extensions;

namespace TaskTracker.Core.CrossCuttingConcerns.Validation
{
    public static class ValidationTool
    {
        public static void Validate(IValidator validator, object entity)
        {
            var context = new ValidationContext<object>(entity);
            var result = validator.Validate(context);
            if (!result.IsValid)
            {
                throw ValidationFieldError.CreateException(result.Errors);
            }


        }
    }
}
