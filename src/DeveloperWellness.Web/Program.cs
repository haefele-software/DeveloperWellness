using DeveloperWellness.Application;
using DeveloperWellness.Domain.Options;
using DeveloperWellness.Infrastructure;
using DeveloperWellness.Web.Components;
using DeveloperWellness.Web.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMemoryCache();

// Wellness options: bound from configuration and validated fail-fast at start-up (tasks.md T009).
builder.Services.AddSingleton<IValidateOptions<WellnessOptions>, WellnessOptionsValidation>();
builder.Services
    .AddOptions<WellnessOptions>()
    .Bind(builder.Configuration.GetSection(WellnessOptions.SectionName))
    .ValidateOnStart();

// One-time wiring of the Application and Infrastructure DI extension methods. Later service and
// adapter tasks append registrations inside those two files; this file is never edited again.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Web-layer shell state services (DashboardState etc.).
builder.Services.AddWebShell();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
