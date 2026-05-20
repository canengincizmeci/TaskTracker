using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace TaskTracker.Core.Utilities.IoC
{
    public interface ICoreModule
    {
        void Load(IServiceCollection services);
    }
}
