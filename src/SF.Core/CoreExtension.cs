﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.CodeAnalysis;
using SF.Core.Extensions;
using SF.Core.Services;
using SF.Core.Settings;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SF.Core.Data;
using SF.Core.Interceptors;
using SF.Core.Entitys;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.SqlServer;
using Microsoft.AspNetCore.Mvc.Razor;
using SF.Core.Web;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Net.Http.Headers;
using SF.Core.Common.Razor;
using SF.Core.Web.Base.Business;
using SF.Core.Errors;
using SF.Core.Web.Formatters.CsvImportExport;
using SF.Core.Web.Attributes;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using SF.Core.Abstraction.UoW;
using SF.Core.EFCore.UoW;
using SF.Core.Common.Message.Email;
using SF.Core.Common.Message.Sms;
using SF.Core.Data.Repository;
using SF.Core.EFCore.Repository;
using Newtonsoft.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Formatters;
using SF.Core.Services.Implementation;
using SF.Core.WorkContexts;
using SF.Core.WorkContexts.Implementation;
using SF.Core.Web.Api.JsonConverters;
using Newtonsoft.Json;
using SF.Core.Web.Middleware;
using SF.Core.Abstraction.Interceptors;
using AutoMapper;
using SF.Core.Abstraction.Mapping;

namespace SF.Core
{
    public class CoreExtension : ModuleInitializerBase
    {
        public override IEnumerable<KeyValuePair<int, Action<IServiceCollection>>> ConfigureServicesActionsByPriorities
        {
            get
            {
                return new Dictionary<int, Action<IServiceCollection>>()
                {
                    [0] = this.AddStaticFiles,
                    [1] = this.AddCustomizedDataStore,
                    [2] = this.AddCoreServices,
                    //  [3] = this.AddDistributedCache,
                    [4] = this.AddMvc
                };
            }
        }

        public override IEnumerable<KeyValuePair<int, Action<IApplicationBuilder>>> ConfigureActionsByPriorities
        {
            get
            {
                return new Dictionary<int, Action<IApplicationBuilder>>()
                {
                    [0] = this.UseStaticFiles,
                    [1] = this.UseCustomizedRequestLocalization,
                    [2] = this.UseMvc,
                };
            }
        }

        #region MyRegion IServiceCollection
        /// <summary>
        /// 将项目文件变成内嵌资源
        /// </summary>
        /// <param name="services"></param>
        private void AddStaticFiles(IServiceCollection services)
        {
            this.serviceProvider.GetService<IHostingEnvironment>().WebRootFileProvider = this.CreateCompositeFileProvider();
        }
        /// <summary>
        /// 注册MVC服务
        /// </summary>
        /// <param name="services"></param>
        private void AddMvc(IServiceCollection services)
        {
            IMvcBuilder mvcBuilder = services.AddMvc();
            mvcBuilder.AddMvcOptions(options =>
            {
                options.Filters.AddService(typeof(HandlerExceptionFilter));
            });


            foreach (var module in ExtensionManager.Modules)
                // Register controller from modules
                mvcBuilder.AddApplicationPart(module.Assembly);

            mvcBuilder.AddRazorOptions(
              o =>
              {
                  foreach (Assembly assembly in ExtensionManager.Assemblies)
                      o.FileProviders.Add(new EmbeddedFileProvider(assembly, assembly.GetName().Name));
                  foreach (var module in ExtensionManager.Modules)
                  {
                      o.AdditionalCompilationReferences.Add(MetadataReference.CreateFromFile(module.Assembly.Location));
                  }
              }
            ).AddViewLocalization()
                .AddDataAnnotationsLocalization();

            foreach (Action<IMvcBuilder> prioritizedAddMvcAction in this.GetPrioritizedAddMvcActions())
            {
                this.logger.LogInformation("Executing prioritized AddMvc action '{0}' of {1}", this.GetActionMethodInfo(prioritizedAddMvcAction));
                prioritizedAddMvcAction(mvcBuilder);
            }
            //Csv
            var csvFormatterOptions = new CsvFormatterOptions();
            mvcBuilder.AddMvcOptions(options =>
            {
                options.InputFormatters.Add(new CsvInputFormatter(csvFormatterOptions));
                options.OutputFormatters.Add(new CsvOutputFormatter(csvFormatterOptions));
                options.FormatterMappings.SetMediaTypeMappingForFormat("csv", MediaTypeHeaderValue.Parse("text/csv"));

                options.RespectBrowserAcceptHeader = true;
                options.InputFormatters.Add(new XmlSerializerInputFormatter());
                options.OutputFormatters.Add(new XmlSerializerOutputFormatter());
            });
            // Force Camel Case to JSON
            mvcBuilder.AddJsonOptions(opts =>
            {
                opts.SerializerSettings.ContractResolver = new BaseContractResolver();
                opts.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                opts.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                opts.SerializerSettings.Converters.Add(new TimeSpanConverter());
                opts.SerializerSettings.Converters.Add(new GuidConverter());
                opts.SerializerSettings.Formatting = Formatting.None;
            });

        }
        /// <summary>
        /// 配置数据库链接 
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public void AddCustomizedDataStore(IServiceCollection services)
        {

            //services.AddDbContext<CoreDbContext>(options =>
            //    options.UseSqlServer(this.configurationRoot.GetConnectionString("DefaultConnection"),
            //        b => b.MigrationsAssembly("SF.WebHost")));
            //services.AddDbContext<CoreDbContext>(options =>
            //    options.UseMySql(configuration.GetConnectionString("MMysqlDatabase"),
            //        b => b.MigrationsAssembly("SF.WebHost")));
            services.AddEntityFrameworkSqlServer()
               .AddDbContext<CoreDbContext>((serviceProvider, options) =>
               options.UseSqlServer(this.configurationRoot.GetConnectionString("DefaultConnection"),
                    b => b.MigrationsAssembly("SF.WebHost"))
                      .UseInternalServiceProvider(serviceProvider));

            //services.AddEntityFrameworkMySql()
            // .AddDbContext<CoreDbContext>((serviceProvider, options) =>
            // options.UseMySql(this.configurationRoot.GetConnectionString("DefaultConnection"))
            //        .UseInternalServiceProvider(serviceProvider)
            //        );
            //services.AddEntityFrameworkNpgsql()
            //  .AddDbContext<CoreDbContext>((serviceProvider, options) =>
            //  options.UseNpgsql(this.configurationRoot.GetConnectionString("DefaultConnection"))
            //         .UseInternalServiceProvider(serviceProvider)
            //         );
            services.AddSingleton<CoreDbContext>();
            services.AddSingleton<DbContext, CoreDbContext>();

        }
        /// <summary>
        /// 添加TagHelper分布式缓存配置
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public void AddDistributedCache(IServiceCollection services)
        {
            //http://www.davepaquette.com/archive/2016/05/22/ASP-NET-Core-Distributed-Cache-Tag-Helper.aspx
            services.AddSingleton<IDistributedCache>(serviceProvider =>
                new SqlServerCache(new SqlServerCacheOptions()
                {
                    ConnectionString = this.configurationRoot.GetConnectionString("DefaultConnection"),
                    SchemaName = "dbo",
                    TableName = "Core_TagDistributedCache"
                }));

        }
        /// <summary>
        /// 添加全局服务注册
        /// </summary>
        /// <param name="services"></param>
        public void AddCoreServices(IServiceCollection services)
        {
            //在使用session之前要注入cacheing，因为session依赖于cache进行存储
            services.AddDistributedMemoryCache(); // Adds a default in-memory implementation of IDistributedCache
            services.AddSession();

            services.Configure<RazorViewEngineOptions>(options => { options.ViewLocationExpanders.Add(new ModuleViewLocationExpander()); });
            // 确保数据库创建和初始数据
            // CoreEFStartup.InitializeDatabaseAsync(app.ApplicationServices).Wait();
            //Identity配置
            services.AddScoped<SignInManager<UserEntity>, SimpleSignInManager<UserEntity>>();
            //注意 必须在AddMvc之前注册
            services.AddIdentity<UserEntity, RoleEntity>(configure =>
            {
                //配置身份选项
                configure.Password.RequireDigit = false;//是否需要数字(0-9).
                configure.Password.RequireLowercase = false;//是否需要小写字母(a-z).
                configure.Password.RequireUppercase = false;//是否需要大写字母(A-Z).
                configure.Password.RequireNonAlphanumeric = false;//是否包含非字母或数字字符。
                configure.Password.RequiredLength = 6;//设置密码长度最小为6
                                                      //  configure.Cookies.ApplicationCookie.LoginPath = "/login";
                configure.Cookies.ApplicationCookie.AuthenticationScheme = "Cookies";
                configure.Cookies.ApplicationCookie.LoginPath = new PathString("/login");
                configure.Cookies.ApplicationCookie.AccessDeniedPath = new PathString("/account/forbidden");
                configure.Cookies.ApplicationCookie.AutomaticAuthenticate = true;
                configure.Cookies.ApplicationCookie.AutomaticChallenge = true;
            })
              .AddRoleStore<SimplRoleStore>()
              .AddUserStore<SimplUserStore>()
             .AddDefaultTokenProviders();


            services.AddSingleton<ViewRenderer>();

            services.AddSingleton<IInterceptor, AuditableInterceptor>();
        

            services.AddScoped<HandlerExceptionFilter>();
            services.TryAddSingleton<IActionContextAccessor, ActionContextAccessor>();
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddSingleton<ICurrentUser, CurrentUser>();
            services.AddSingleton<IUserNameResolver, UserNameResolver>();

            services.AddTransient(typeof(IEFCoreQueryableRepository<>), typeof(EFCoreBaseRepository<>));
            services.AddTransient(typeof(IEFCoreQueryableRepository<,>), typeof(EFCoreBaseRepository<,>));

            services.AddSingleton<IBaseUnitOfWork, BaseUnitOfWork>();
            //(sp =>
            //{
            //    var simpleDbContext = sp.GetService<CoreDbContext>();
            //    var userNameResolver = sp.GetService<IUserNameResolver>();
            //    return new BaseUnitOfWork(simpleDbContext);
            //});
            services.AddSingleton<IUnitOfWork>(sp => sp.GetService<IBaseUnitOfWork>());
            services.AddSingleton<IUnitOfWorkFactory>(uow => new UnitOfWorkFactory(services.BuildServiceProvider()));

            services.TryAddScoped<IWorkContext, WorkContext>();
            services.TryAddScoped<ISiteContext, SiteContext>();
            services.TryAddScoped<IMediaService, LocalMediaService>();
            services.TryAddScoped<IUrlSlugService, UrlSlugService>();
            services.TryAddScoped<ISettingsManager, SettingsManager>();
            services.TryAddScoped<ISiteMessageEmailSender, SiteEmailMessageSender>();
            services.TryAddScoped<ISmsSender, SiteSmsSender>();

            services.TryAddScoped<IExceptionMapper, BaseExceptionMapper>();
            services.AddTransient(typeof(ICodetableWriter<,>), typeof(CodeTableWriter<,>));
            services.AddTransient(typeof(ICodetableReader<,>), typeof(CodetableReader<,>));

        }


        private Action<IMvcBuilder>[] GetPrioritizedAddMvcActions()
        {
            List<KeyValuePair<int, Action<IMvcBuilder>>> addMvcActionsByPriorities = new List<KeyValuePair<int, Action<IMvcBuilder>>>();

            foreach (IModuleInitializer extension in ExtensionManager.Extensions)
                if (extension is IModuleInitializer)
                    if ((extension as IModuleInitializer).AddMvcActionsByPriorities != null)
                        addMvcActionsByPriorities.AddRange((extension as IModuleInitializer).AddMvcActionsByPriorities);

            return this.GetPrioritizedActions(addMvcActionsByPriorities);
        }
        #endregion

        #region MyRegion IApplicationBuilder


        /// <summary>
        /// 配置多语言信息
        /// </summary>
        /// <param name="app"></param>
        /// <returns></returns>
        public void UseCustomizedRequestLocalization(IApplicationBuilder app)
        {
            var supportedCultures = new[]
            {
                new CultureInfo("zh"),
                new CultureInfo("en"),
            };
            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("en", "en"),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            });

        }
        /// <summary>
        /// 配置静态文件
        /// </summary>
        /// <param name="applicationBuilder"></param>
        private void UseStaticFiles(IApplicationBuilder applicationBuilder)
        {
            applicationBuilder.UseStaticFiles();

            //模块的静态文件
            foreach (var module in ExtensionManager.Modules)
            {
                var wwwrootDir = new DirectoryInfo(Path.Combine(module.Path, "wwwroot"));
                if (!wwwrootDir.Exists)
                {
                    continue;
                }

                applicationBuilder.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(wwwrootDir.FullName),
                    //默认模块.分隔符最后的名称 如（SF.Module.Web）/Core
                    RequestPath = new PathString("/" + module.ShortName)
                });
            }
        }

        private void UseMvc(IApplicationBuilder applicationBuilder)
        {
            // 重要: session的注册必须在UseMvc之前，因为MVC里面要用 
            applicationBuilder.UseSession(new SessionOptions() { IdleTimeout = TimeSpan.FromMinutes(30) });
            applicationBuilder.UseIdentity();


            applicationBuilder.UseMiddleware<CurrentUserMiddleware>();
            // applicationBuilder.UseMiddleware<RequestLoggerMiddleware>();
            //  applicationBuilder.UseMiddleware<ProcessingTimeMiddleware>();
            //注册MVC请求
            applicationBuilder.UseMvc(
              routeBuilder =>
              {
                  routeBuilder.Routes.Add(new UrlSlugRoute(routeBuilder.DefaultHandler));

                  routeBuilder.MapRoute(
                 "default",
                 "{controller=Home}/{action=Index}/{id?}");

                  foreach (Action<IRouteBuilder> prioritizedUseMvcAction in this.GetPrioritizedUseMvcActions())
                  {
                      this.logger.LogInformation("Executing prioritized UseMvc action '{0}' of {1}", this.GetActionMethodInfo(prioritizedUseMvcAction));
                      prioritizedUseMvcAction(routeBuilder);
                  }


              }
            );

            applicationBuilder.UseCors(builder =>
            {
                builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader().AllowCredentials();
            });

         
            //  applicationBuilder.UseMultitenancy<SiteContext>();
            //多租户
            // var storage = configurationRoot["DevOptions:DbPlatform"];
            //    applicationBuilder.UsePerTenant<SiteContext>((ctx, builder) =>
            //    {
            //        // custom 404 and error page - this preserves the status code (ie 404)
            //        if (string.IsNullOrEmpty(ctx.Tenant.SiteFolderName))
            //        {
            //            builder.UseStatusCodePagesWithReExecute("/Home/Error/{0}");
            //        }
            //        else
            //        {
            //            builder.UseStatusCodePagesWithReExecute("/" + ctx.Tenant.SiteFolderName + "/Home/Error/{0}");
            //        }

            //        // todo how to make this multi tenant for folders?
            //        // https://github.com/IdentityServer/IdentityServer4/issues/19
            //        //https://github.com/IdentityServer/IdentityServer4/blob/dev/src/IdentityServer4/Configuration/IdentityServerApplicationBuilderExtensions.cs
            //        //https://github.com/IdentityServer/IdentityServer4/blob/dev/src/IdentityServer4/Hosting/IdentityServerMiddleware.cs
            //        // perhaps will need to plugin custom IEndpointRouter?
            //        if (storage == "ef")
            //        {
            //            // with this uncommented it breaks folder tenants
            //            // builder.UseIdentityServer();

            //            // this sets up the authentication for apis within this endpoint
            //            // ie apis that are hosted in the same web app endpoint with the authority server
            //            // this is not needed here if you are only using separate api endpoints
            //            // it is needed in the startup of those separate endpoints
            //            applicationBuilder.UseIdentityServerAuthentication(new IdentityServerAuthenticationOptions
            //            {
            //                Authority = "https://localhost:44399",
            //                // using the site aliasid as the scope so each tenant has a different scope
            //                // you can view the aliasid from site settings
            //                // clients must be configured with the scope to have access to the apis for the tenant
            //                ScopeName = ctx.Tenant.AliasId,

            //                RequireHttpsMetadata = true
            //            });

            //        }



            //    });


        }

        private Action<IRouteBuilder>[] GetPrioritizedUseMvcActions()
        {
            List<KeyValuePair<int, Action<IRouteBuilder>>> useMvcActionsByPriorities = new List<KeyValuePair<int, Action<IRouteBuilder>>>();

            foreach (IModuleInitializer extension in ExtensionManager.Extensions)
                if (extension is IModuleInitializer)
                    if ((extension as IModuleInitializer).UseMvcActionsByPriorities != null)
                        useMvcActionsByPriorities.AddRange((extension as IModuleInitializer).UseMvcActionsByPriorities);

            return this.GetPrioritizedActions(useMvcActionsByPriorities);
        }

        private IFileProvider CreateCompositeFileProvider()
        {
            IFileProvider[] fileProviders = new IFileProvider[] {
        this.serviceProvider.GetService<IHostingEnvironment>().WebRootFileProvider
      };

            return new CompositeFileProvider(
              fileProviders.Concat(
                ExtensionManager.Assemblies.Select(a => new EmbeddedFileProvider(a, a.GetName().Name))
              )
            );
        }

        private Action<T>[] GetPrioritizedActions<T>(IEnumerable<KeyValuePair<int, Action<T>>> actionsByPriorities)
        {
            return actionsByPriorities
              .OrderBy(actionByPriority => actionByPriority.Key)
              .Select(actionByPriority => actionByPriority.Value)
              .ToArray();
        }

        private string[] GetActionMethodInfo<T>(Action<T> action)
        {
            MethodInfo methodInfo = action.GetMethodInfo();

            return new string[] { methodInfo.Name, methodInfo.DeclaringType.FullName };
        }

        #endregion
    }
}