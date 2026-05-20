using Autofac;
using Autofac.Core;
using Autofac.Extras.DynamicProxy;
using Castle.DynamicProxy;
using DrivingCourse.Core.Utilities.Interceptors;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Utilities.Security.Jwt;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.DataAccess.Concrete.EfCore;

namespace TaskTracker.Bussiness.DependencyResolvers.Autofac
{
    public class AutofacBusinessModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<UnitOfWork>()
                .As<IUnitOfWork>()
                .InstancePerLifetimeScope();

            builder.RegisterType<UserManager>()
                .As<IUserService>()
                .InstancePerLifetimeScope();

            builder.RegisterType<EfUserDal>()
                .As<IUserDal>()
                .InstancePerLifetimeScope();

            builder.RegisterType<AuthManager>()
                .As<IAuthService>()
                .InstancePerLifetimeScope();

            builder.RegisterType<JwtHelper>()
                .As<ITokenHelper>()
                .InstancePerLifetimeScope();

            builder.RegisterType<EmailManager>()
                .As<IEmailService>()
                .InstancePerLifetimeScope();

            builder.RegisterType<HttpContextAccessor>()
                .As<IHttpContextAccessor>()
                .SingleInstance();

          

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();

            builder.RegisterAssemblyTypes(assembly)
                .AsImplementedInterfaces()
                .EnableInterfaceInterceptors(new ProxyGenerationOptions
                {
                    Selector = new AspectInterceptorSelector()
                })
                .InstancePerLifetimeScope();





            //base.Load(builder);
        }


    }
}
