using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.ValidationRules.FluentValidation
{
    public class TaskRequestValidator:AbstractValidator<TaskRequestDto>
    {
        public TaskRequestValidator()
        {
            
        }
    }
}
