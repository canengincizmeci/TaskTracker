using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Entities.DTOs;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Bussiness.ValidationRules.FluentValidation
{
    public class UpdateTaskRequestDtoValidator : AbstractValidator<UpdateTaskRequestDto>
    {
        public UpdateTaskRequestDtoValidator()
        {
            RuleFor(x => x.Id).GreaterThan(0);

            RuleFor(x => x.Title).NotEmpty().MinimumLength(3).MaximumLength(100);

            RuleFor(x => x.Description).NotEmpty().MinimumLength(5).MaximumLength(1000);

            RuleFor(x => x.Category).NotEmpty().MaximumLength(TaskRequest.MaxCategoryLength);
            RuleFor(x => x.UtcOffsetMinutes).InclusiveBetween(TaskCalendar.MinOffsetMinutes, TaskCalendar.MaxOffsetMinutes);

            RuleFor(x => x.Priority).IsInEnum();

        }
    }
}
