using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.ValidationRules.FluentValidation
{
    public class TaskRequestCreateDtoValidator : AbstractValidator<TaskRequestCreateDto>
    {
        public TaskRequestCreateDtoValidator()
        {
            RuleFor(x => x.Title).NotEmpty().MinimumLength(3).MaximumLength(100);

            RuleFor(x => x.Description).NotEmpty().MinimumLength(5).MaximumLength(1000);

            RuleFor(x => x.Category).NotEmpty();

            RuleFor(x => x.Priority).IsInEnum();

            RuleFor(x => x.Status).IsInEnum();

            RuleFor(x => x.DueDate).GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow)).When(x => x.DueDate.HasValue);
        }
    }
}
 