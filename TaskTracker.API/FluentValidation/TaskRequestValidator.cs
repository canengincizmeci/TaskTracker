using FluentValidation;
using TaskTracker.API.DTOs;

namespace TaskTracker.API.FluentValidation
{
    public class TaskRequestValidator : AbstractValidator<TaskRequestDto>
    {
        public TaskRequestValidator()
        {
            RuleFor(tr => tr.Title).NotNull().NotEmpty().MaximumLength(100).MinimumLength(5);
            RuleFor(tr => tr.Description).NotNull().NotEmpty().MaximumLength(2000).MinimumLength(5);







        }
    }
}
