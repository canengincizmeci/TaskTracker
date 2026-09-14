using FluentValidation;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.ValidationRules.FluentValidation
{
    public class UpdateTaskStatusDtoValidator : AbstractValidator<UpdateTaskStatusDto>
    {
        public UpdateTaskStatusDtoValidator()
        {
            RuleFor(x => x.Id).GreaterThan(0);

            RuleFor(x => x.Status).NotNull().IsInEnum();
        }
    }
}
