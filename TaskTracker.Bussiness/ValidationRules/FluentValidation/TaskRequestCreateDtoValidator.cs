using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Entities.DTOs;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Bussiness.ValidationRules.FluentValidation
{
    public class TaskRequestCreateDtoValidator : AbstractValidator<TaskRequestCreateDto>
    {
        public TaskRequestCreateDtoValidator()
        {
            RuleFor(x => x.Title).NotEmpty().MinimumLength(3).MaximumLength(100);

            RuleFor(x => x.Description).NotEmpty().MinimumLength(5).MaximumLength(1000);

            RuleFor(x => x.Category).NotEmpty().MaximumLength(TaskRequest.MaxCategoryLength);

            RuleFor(x => x.Priority).IsInEnum();

            RuleFor(x => x.Status).IsInEnum();

            RuleFor(x => x.UtcOffsetMinutes).InclusiveBetween(TaskCalendar.MinOffsetMinutes, TaskCalendar.MaxOffsetMinutes);
            RuleFor(x => x.DueDate)
                .Must((task, dueDate) => dueDate is null || dueDate >= TaskCalendar.LocalDay(DateTime.UtcNow, task.UtcOffsetMinutes))
                .WithMessage("Due date must be today or later.")
                .When(x => x.UtcOffsetMinutes is >= TaskCalendar.MinOffsetMinutes and <= TaskCalendar.MaxOffsetMinutes);
        }
    }
}
