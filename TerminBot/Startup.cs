using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using TerminBot.Bots;
using TerminBot.Adapters;
using Microsoft.Bot.Builder.Dialogs;
using TerminBot.Dialogs;
using Microsoft.EntityFrameworkCore;
using TerminBot.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using TerminBot.Models;
using TerminBot.Security;




namespace TerminBot
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();


            services.AddSingleton<IStorage, MemoryStorage>();
            services.AddSingleton<UserState>();
            services.AddSingleton<ConversationState>();

            //webchat
            services.AddSingleton<LocalAdapter>();


            services.AddScoped<ServiceRequestDialog>();
            services.AddScoped<MainDialog>();
            services.AddScoped<ReservationDialog>();
            services.AddTransient<IBot, DialogBot<MainDialog>>();


            services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();


            services.AddControllersWithViews();


            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite("Data Source=servicedb.sqlite"));



            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(o =>
                        {
                            o.LoginPath = "/auth/login";
                            o.LogoutPath = "/auth/logout";
                            o.AccessDeniedPath = "/auth/login";
                            o.SlidingExpiration = true;
                            o.ExpireTimeSpan = TimeSpan.FromHours(8);
                        });

            services.AddAuthorization();

            services.AddSession(o => { o.Cookie.HttpOnly = true; o.Cookie.IsEssential = true; });



        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseDefaultFiles();
            app.UseStaticFiles();


            app.UseRouting();

            app.UseSession();

            app.UseAuthentication();
            app.UseAuthorization();


            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();

                endpoints.MapControllerRoute(
                    name: "chat",
                    pattern: "Chat/{action=Index}/{id?}",
                    defaults: new { controller = "Chat", action = "Index" });


                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Admin}/{action=Index}/{id?}");
                        });


            using (var scope = app.ApplicationServices.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();

                var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var u = cfg["AdminSeed:Username"];
                var p = cfg["AdminSeed:Password"];

                if (!string.IsNullOrWhiteSpace(u) && !string.IsNullOrWhiteSpace(p))
                {
                    if (!db.AdminUsers.Any(x => x.Username == u))
                    {
                        db.AdminUsers.Add(new AdminUser
                        {
                            Username = u,
                            PasswordHash = PasswordHasher.Hash(p)
                        });
                        db.SaveChanges();
                    }
                }
            }



        }
    }
}
