using FluentValidation;

namespace TaskTracker.Bussiness.CrossCuttingConcerns.Validation
{
    public class ValidationTool
    {
        public static void Validate(IValidator validator, object entity)
            => TaskTracker.Core.CrossCuttingConcerns.Validation.ValidationTool.Validate(validator, entity);
    }
}
