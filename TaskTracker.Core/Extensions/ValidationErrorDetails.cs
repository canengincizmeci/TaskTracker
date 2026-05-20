using System;
using System.Collections.Generic;
using System.Text;
using FluentValidation.Results;

namespace TaskTracker.Core.Extensions
{
    public class ValidationErrorDetails:ErrorDetails
    {
        public IEnumerable<ValidationFailure>? Errors { get; set; }
    }
}
