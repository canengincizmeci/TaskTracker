using Castle.DynamicProxy;
using System.Reflection;
using TaskTracker.Core.Utilities.Interceptors;
using IInterceptor = Castle.DynamicProxy.IInterceptor;

namespace TaskTracker.Core.Utilities.Interceptors
{
    public class AspectInterceptorSelector : IInterceptorSelector
    {
        public IInterceptor[] SelectInterceptors(Type type, MethodInfo method, IInterceptor[] interceptors)
        {
            var classAttributes = type
                .GetCustomAttributes<MethodInterceptionBaseAttribute>(true)
                .ToList();

            var methodAttributes = method
                .GetCustomAttributes<MethodInterceptionBaseAttribute>(true)
                .ToList();

            var implementationMethod = type.GetMethod(
                method.Name,
                method.GetParameters().Select(p => p.ParameterType).ToArray());

            if (implementationMethod != null)
            {
                var implementationMethodAttributes = implementationMethod
                    .GetCustomAttributes<MethodInterceptionBaseAttribute>(true);

                methodAttributes.AddRange(implementationMethodAttributes);
            }

            classAttributes.AddRange(methodAttributes);

            return classAttributes
                .OrderBy(x => x.Priority)
                .ToArray();
        }
    }
}