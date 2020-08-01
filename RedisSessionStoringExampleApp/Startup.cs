using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AuthenticationApp;
using AuthenticationApp.Jwt;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using RedisSessionStoringExampleApp.Data;
using RedisSessionStoringExampleApp.Data.UsersRepository;
using RedisSessionStoringExampleApp.Models;
using RepositoriesApp;

namespace RedisSessionStoringExampleApp
{
    public class Startup
    {
        public IConfiguration Configuration { get; set; }

        private AuthenticationOptions _JwtauthenticationOptions;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            _JwtauthenticationOptions = new AuthenticationOptions()
            {
                Issuer = "zZen.Server",
                Audience = "zZen.Client",
                Lifetime = 1, // min
                Key = Configuration.GetValue<string>("SecretKey")
            };
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();

            services.AddDbContext<UserDbContext>(options =>
                options.UseSqlServer(Configuration.GetConnectionString("defaultConn")));

            services.AddSingleton<IRepository<User>>(impl =>
                new EFUserRepository(impl.GetService<UserDbContext>()));

            services.AddDistributedRedisCache(config =>
            {
                config.Configuration = "localhost";
            });

            services.AddSession(config =>
            {
                config.Cookie.HttpOnly = false;
                config.Cookie.SameSite = SameSiteMode.Lax;
                config.Cookie.Name = "zZen.Cookies";
                config.IdleTimeout = TimeSpan.FromSeconds(120);
            });

            services.AddAntiforgery(config =>
            {
                config.Cookie.Name = "zZen.AntiforgeryCookies";
                config.Cookie.HttpOnly = true;
                config.Cookie.SameSite = SameSiteMode.Strict;

                config.FormFieldName = "AntiforgeryField";
                config.HeaderName = "X-CSRF-TOKEN-HEADERNAME";
                config.SuppressXFrameOptionsHeader = false;
            });

            services.AddAuthentication(config =>
            {
                config.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                config.DefaultSignInScheme = JwtBearerDefaults.AuthenticationScheme;
                config.DefaultSignOutScheme = JwtBearerDefaults.AuthenticationScheme;
            })
                .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, cookieConfig =>
                {
                    cookieConfig.LoginPath = "/Admin/Login";
                    cookieConfig.LogoutPath = "/Admin/Logout";
                    cookieConfig.Cookie.HttpOnly = true;
                    cookieConfig.Cookie.SameSite = SameSiteMode.Strict;
                })
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.RequireHttpsMetadata = true;
                    options.Audience = _JwtauthenticationOptions.Audience;
                    options.SaveToken = true;
                    options.TokenValidationParameters = new TokenValidationParameters()
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_JwtauthenticationOptions.Key)),
                        ValidateIssuer = true,
                        ValidIssuer = _JwtauthenticationOptions.Issuer,
                        ValidateAudience = true,
                        ValidAudience = _JwtauthenticationOptions.Audience,
                        ValidateLifetime = true,
                    };
                });

            services.AddAuthorization(config =>
            {
                config.AddPolicy("admin", policyCfg =>
                {
                    policyCfg.RequireRole("admin");
                    policyCfg.RequireAuthenticatedUser();
                });
            });

            services.AddSingleton<IRefreshTokenGenerator, JwtRefreshTokenGenerator>();
            
            services.AddSingleton<IAuthenticationManager>(impl =>
                new JwtAuthenticationManager(impl.GetService<IRefreshTokenGenerator>(), _JwtauthenticationOptions));

            services.AddSingleton<ITokenRefresher>(impl =>
                new JwtTokenRefresher(_JwtauthenticationOptions, impl.GetService<JwtAuthenticationManager>()));
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IAntiforgery antiforgery)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHsts();
            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseAntiforgery(antiforgery);

            app.UseJwtAuthentication();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseSession();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapControllerRoute(
                    "default",
                    "{controller}/{action}/{param?}");
            });
        }
    }
}
