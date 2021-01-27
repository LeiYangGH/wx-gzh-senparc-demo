using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Senparc.CO2NET;
using Senparc.CO2NET.AspNet;
using Senparc.CO2NET.Cache;
using Senparc.CO2NET.Cache.Memcached;//DPBMARK Memcached DPBMARK_END
using Senparc.CO2NET.Utilities;
using Senparc.NeuChar.MessageHandlers;
using Senparc.Weixin.Cache.Memcached;//DPBMARK Memcached DPBMARK_END
using Senparc.Weixin.Entities;
using Senparc.Weixin.MP;//DPBMARK MP DPBMARK_END
using Senparc.Weixin.MP.MessageHandlers.Middleware;//DPBMARK MP DPBMARK_END
using Senparc.Weixin.MP.Sample.CommonService.CustomMessageHandler;//DPBMARK MP DPBMARK_END
using Senparc.Weixin.MP.Sample.CommonService.MessageHandlers.WebSocket;//DPBMARK WebSocket DPBMARK_END
using Senparc.Weixin.MP.Sample.CommonService.WorkMessageHandlers;//DPBMARK Work DPBMARK_END
using Senparc.Weixin.MP.Sample.CommonService.WxOpenMessageHandler;//DPBMARK MiniProgram DPBMARK_END
using Senparc.Weixin.RegisterServices;
using System;
using System.IO;
using System.Text;

namespace Senparc.Weixin.Sample.NetCore3
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSession();//使用Session（实践证明需要在配置 Mvc 之前）

            services.AddControllersWithViews()
                    .AddNewtonsoftJson()// 支持 NewtonsoftJson
                    .SetCompatibilityVersion(Microsoft.AspNetCore.Mvc.CompatibilityVersion.Version_3_0);
            // Add CookieTempDataProvider after AddMvc and include ViewFeatures.
            services.AddSingleton<ITempDataProvider, CookieTempDataProvider>();

            //如果部署在linux系统上，需要加上下面的配置：
            //services.Configure<KestrelServerOptions>(options => options.AllowSynchronousIO = true);
            //如果部署在IIS上，需要加上下面的配置：
            services.Configure<IISServerOptions>(options => options.AllowSynchronousIO = true);

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddMemoryCache();//使用本地缓存必须添加


            /*
             * CO2NET 是从 Senparc.Weixin 分离的底层公共基础模块，经过了长达 6 年的迭代优化，稳定可靠。
             * 关于 CO2NET 在所有项目中的通用设置可参考 CO2NET 的 Sample：
             * https://github.com/Senparc/Senparc.CO2NET/blob/master/Sample/Senparc.CO2NET.Sample.netcore/Startup.cs
             */


            services.AddSenparcWeixinServices(Configuration)//Senparc.Weixin 注册（必须）
                    ;

            //services.AddCertHttpClient("name", "pwd", "path");//此处可以添加更多 Cert 证书
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env,
                IOptions<SenparcSetting> senparcSetting, IOptions<SenparcWeixinSetting> senparcWeixinSetting)
        {
            //启用 GB2312（按需）
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            //引入EnableRequestRewind中间件（按需）
            app.UseEnableRequestRewind();//DPBMARK MP DPBMARK_END
            //使用 Session（按需，本示例中需要用到）
            app.UseSession();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();


            // 启动 CO2NET 全局注册，必须！
            // 关于 UseSenparcGlobal() 的更多用法见 CO2NET Demo：https://github.com/Senparc/Senparc.CO2NET/blob/master/Sample/Senparc.CO2NET.Sample.netcore3/Startup.cs
            var registerService = app.UseSenparcGlobal(env, senparcSetting.Value, globalRegister =>
                {
                    #region CO2NET 全局配置

                    #region 全局缓存配置（按需）

                    //当同一个分布式缓存同时服务于多个网站（应用程序池）时，可以使用命名空间将其隔离（非必须）
                    globalRegister.ChangeDefaultCacheNamespace("DefaultCO2NETCache");


                    #region 配置和使用 Memcached      -- DPBMARK Memcached

                    //配置Memcached缓存（按需，独立）
                    if (UseMemcached(senparcSetting.Value, out string memcachedConfigurationStr)) //这里为了方便不同环境的开发者进行配置，做成了判断的方式，实际开发环境一般是确定的，这里的if条件可以忽略
                    {
                        app.UseEnyimMemcached();

                        /* 说明：
                        * 1、Memcached 的连接字符串信息会从 Config.SenparcSetting.Cache_Memcached_Configuration 自动获取并注册，如不需要修改，下方方法可以忽略
                    /* 2、如需手动修改，可以通过下方 SetConfigurationOption 方法手动设置 Memcached 链接信息（仅修改配置，不立即启用）
                        */
                        Senparc.CO2NET.Cache.Memcached.Register.SetConfigurationOption(memcachedConfigurationStr);

                        //以下会立即将全局缓存设置为 Memcached
                        Senparc.CO2NET.Cache.Memcached.Register.UseMemcachedNow();

                        //也可以通过以下方式自定义当前需要启用的缓存策略
                        CacheStrategyFactory.RegisterObjectCacheStrategy(() => MemcachedObjectCacheStrategy.Instance);
                    }

                    #endregion                        //  DPBMARK_END

                    #endregion

                    #region 注册日志（按需，建议）

                    globalRegister.RegisterTraceLog(ConfigTraceLog);//配置TraceLog

                    #endregion

                    #region APM 系统运行状态统计记录配置

                    //测试APM缓存过期时间（默认情况下可以不用设置）
                    CO2NET.APM.Config.EnableAPM = true;//默认已经为开启，如果需要关闭，则设置为 false
                    CO2NET.APM.Config.DataExpire = TimeSpan.FromMinutes(60);

                    #endregion

                    #endregion
                }, true)
                //使用 Senparc.Weixin SDK
                .UseSenparcWeixin(senparcWeixinSetting.Value, weixinRegister =>
                {
                    #region 微信相关配置

                    /* 微信配置开始
                    * 
                    * 建议按照以下顺序进行注册，尤其须将缓存放在第一位！
                    */

                    #region 微信缓存（按需，必须放在配置开头，以确保其他可能依赖到缓存的注册过程使用正确的配置）
                    //注意：如果使用非本地缓存，而不执行本块注册代码，将会收到“当前扩展缓存策略没有进行注册”的异常


                    // 微信的 Memcached 缓存，如果不使用则注释掉（开启前必须保证配置有效，否则会抛错）    -- DPBMARK Memcached
                    if (UseMemcached(senparcSetting.Value, out _))
                    {
                        app.UseEnyimMemcached();
                        weixinRegister.UseSenparcWeixinCacheMemcached();
                    }                                                                                     // DPBMARK_END

                    #endregion

                    #region 注册公众号或小程序（按需）

                    weixinRegister
                            //注册公众号（可注册多个）                                                    -- DPBMARK MP

                            .RegisterMpAccount(senparcWeixinSetting.Value, "【盛派网络小助手】公众号")     // DPBMARK_END




                            //除此以外，仍然可以在程序任意地方注册公众号或小程序：
                            //AccessTokenContainer.Register(appId, appSecret, name);//命名空间：Senparc.Weixin.MP.Containers
                    #endregion




                        ;

                    /* 微信配置结束 */

                    #endregion
                });

            #region 使用 MessageHadler 中间件，用于取代创建独立的 Controller
            //MessageHandler 中间件介绍：https://www.cnblogs.com/szw/p/Wechat-MessageHandler-Middleware.html

            //使用公众号的 MessageHandler 中间件（不再需要创建 Controller）                       --DPBMARK MP
            app.UseMessageHandlerForMp("/WeixinAsync", CustomMessageHandler.GenerateMessageHandler, options =>
            {
                /* 说明：
                 * 1、此代码块中演示了较为全面的功能点，简化的使用可以参考下面小程序和企业微信
                 * 2、使用中间件也支持多账号，可以使用 URL 添加参数的方式（如：/Weixin?id=1），
                 *    在options.AccountSettingFunc = context => {...} 中，从 context.Request 中获取 [id] 值，
                 *    并反馈对应的 senparcWeixinSetting 信息
                 */

                #region 配置 SenparcWeixinSetting 参数，以自动提供 Token、EncodingAESKey 等参数

                //此处为委托，可以根据条件动态判断输入条件（必须）
                options.AccountSettingFunc = context =>
                //方法一：使用默认配置
                    senparcWeixinSetting.Value;

                //方法二：使用指定配置：
                //Config.SenparcWeixinSetting["<Your SenparcWeixinSetting's name filled with Token, AppId and EncodingAESKey>"]; 

                //方法三：结合 context 参数动态判断返回Setting值

                #endregion

                //对 MessageHandler 内异步方法未提供重写时，调用同步方法（按需）
                options.DefaultMessageHandlerAsyncEvent = DefaultMessageHandlerAsyncEvent.SelfSynicMethod;

                //对发生异常进行处理（可选）
                options.AggregateExceptionCatch = ex =>
                {
                    //逻辑处理...
                    return false;//系统层面抛出异常
                };
            });                                                                                   // DPBMARK_END



            #endregion


            app.UseAuthorization();//需要在注册微信 SDK 之后执行

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });

        }

        /// <summary>
        /// 配置微信跟踪日志（演示，按需）
        /// </summary>
        private void ConfigTraceLog()
        {
            //这里设为Debug状态时，/App_Data/WeixinTraceLog/目录下会生成日志文件记录所有的API请求日志，正式发布版本建议关闭

            //如果全局的IsDebug（Senparc.CO2NET.Config.IsDebug）为false，此处可以单独设置true，否则自动为true
            CO2NET.Trace.SenparcTrace.SendCustomLog("系统日志", "系统启动");//只在Senparc.Weixin.Config.IsDebug = true的情况下生效

            //全局自定义日志记录回调
            CO2NET.Trace.SenparcTrace.OnLogFunc = () =>
            {
                //加入每次触发Log后需要执行的代码
            };

            //当发生基于WeixinException的异常时触发
            WeixinTrace.OnWeixinExceptionFunc = async ex =>
            {
                //加入每次触发WeixinExceptionLog后需要执行的代码

            };
        }


        // -- DPBMARK Memcached
        /// <summary>
        /// 初步判断当前配置是否满足使用 Memcached（根据是否已经修改了默认配置字符串判断）
        /// </summary>
        /// <param name="senparcSetting"></param>
        /// <returns></returns>
        private bool UseMemcached(SenparcSetting senparcSetting, out string memcachedConfigurationStr)
        {
            memcachedConfigurationStr = senparcSetting.Cache_Memcached_Configuration;
            var useMemcached = !string.IsNullOrEmpty(memcachedConfigurationStr) && memcachedConfigurationStr != "#{Cache_Memcached_Configuration}#";
            return useMemcached;
        }
        // -- DPBMARK_END

    }
}
