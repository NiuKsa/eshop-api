﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Swagger;
using eshopAPI.DataAccess;
using eshopAPI.Validators;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Identity;
using eshopAPI.Models;
using eshopAPI.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace eshopAPI
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
            services.AddCors();
            services.AddDbContext<ShopContext>(options =>
                options.UseSqlServer(Configuration.GetConnectionString("EshopConnection")));

            services.AddIdentity<ShopUser, IdentityRole>(opt => { opt.SignIn.RequireConfirmedEmail = true; })
                .AddEntityFrameworkStores<ShopContext>()
                .AddDefaultTokenProviders();

            services.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                o.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(jwtBearerOptions =>
            {
                jwtBearerOptions.TokenValidationParameters = new TokenValidationParameters()
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = Configuration["Token:Issuer"],
                    ValidAudience = Configuration["Token:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["Token:Key"]))
                };
            });

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Info { Title = "eshop-api", Version = "v1" });
                c.AddSecurityDefinition("Bearer", new ApiKeyScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",
                    In = "header",
                    Type = "apiKey"
                });
                c.AddSecurityRequirement(new Dictionary<string, IEnumerable<string>>()
                {
                    {"Bearer", new string[]{ } }
                });
            });

            // register services for DI
            // AddTransient - creates new services for every injection
            // AddScoped - creates and uses same service during request
            // AddSingleton - creates when first time requested and uses same instance all time

            // register data access layer
            services.AddScoped<IItemRepository, ItemRepository>();
            services.AddScoped<ICartRepository, CartRepository>();
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddScoped<ICategoryRepository, CategoryRepository>();
            services.AddScoped<IAttributeRepository, AttributeRepository>();
            services.AddTransient<IEmailSender, EmailSender>();
            services.AddTransient<ITokenGenerator, TokenGenerator>();
            services.AddTransient<IUserClaimsService, UserClaimsService>();
            services.AddSingleton(Configuration);

            services.AddMvc(opt => { opt.Filters.Add(typeof(ValidatorActionFilter)); })
                .AddFluentValidation(fvc => fvc.RegisterValidatorsFromAssemblyContaining<Startup>());
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IServiceProvider serviceProvider)
        {
            if (env.IsDevelopment())
            {
                
            }
            // TODO: remove this afterwards
            app.UseCors(
                options => options.AllowAnyMethod().AllowAnyHeader().AllowCredentials().AllowAnyOrigin()
            );
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "eshop-api V1");
            });
            
            loggerFactory.AddLog4Net();

            app.UseAuthentication();
            CreateRoles(serviceProvider).Wait();
            app.UseMvc();
        }

        private async Task CreateRoles(IServiceProvider serviceProvider)
        {
            //initializing custom roles 
            var RoleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var UserManager = serviceProvider.GetRequiredService<UserManager<ShopUser>>();
            string[] roleNames = { UserRole.Admin.ToString(), UserRole.User.ToString(), UserRole.Blocked.ToString() };
            IdentityResult roleResult;

            foreach (var roleName in roleNames)
            {
                var roleExist = await RoleManager.RoleExistsAsync(roleName);
                if (!roleExist)
                {
                    //create the roles and seed them to the database: Question 1
                    roleResult = await RoleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            //Here you could create a super user who will maintain the web app
            var poweruser = new ShopUser
            {
                UserName = Configuration["AdminUsername"],
                Email = Configuration["AdminUsername"],
            };
            //Ensure you have these values in your appsettings.json file
            string userPWD = Configuration["AdminUserPass"];
            var _user = await UserManager.FindByNameAsync(Configuration["AdminUsername"]);

            if (_user == null)
            {
                var createPowerUser = await UserManager.CreateAsync(poweruser, userPWD);
                var confirmToken = await UserManager.GenerateEmailConfirmationTokenAsync(poweruser);
                await UserManager.ConfirmEmailAsync(poweruser, confirmToken);
                if (createPowerUser.Succeeded)
                {
                    //here we tie the new user to the role
                    await UserManager.AddToRoleAsync(poweruser, UserRole.Admin.ToString());

                }
            }
        }
    }
}
