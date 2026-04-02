using CrTelegramBot.ClashRoyale;
using CrTelegramBot.Configuration;
using CrTelegramBot.Data;
using CrTelegramBot.Services;
using CrTelegramBot.Telegram;
using CrTelegramBot.Workers;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<BotConfig>(builder.Configuration.GetSection(BotConfig.SectionName));
var botConfig = builder.Configuration.GetSection(BotConfig.SectionName).Get<BotConfig>() ?? new BotConfig();

builder.Services.AddDbContext<BotDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5432;Database=crtelegrambot;Username=crbot;Password=crbot"));

builder.Services.AddSingleton(botConfig);
builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botConfig.TelegramBotToken));

builder.Services.AddHttpClient<ClashRoyaleApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.clashroyale.com/v1/");
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", botConfig.ClashRoyaleApiToken);
});

builder.Services.AddScoped<LeaderService>();
builder.Services.AddScoped<UserLinkService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ClanService>();
builder.Services.AddScoped<WarService>();
builder.Services.AddSingleton<AutoDeleteService>();

builder.Services.AddSingleton<CommandParser>();
builder.Services.AddSingleton<BotUpdateHandler>();

builder.Services.AddHostedService<TelegramPollingWorker>();
builder.Services.AddHostedService<ClanMembershipMonitorWorker>();
builder.Services.AddHostedService<WarReminderWorker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
    db.Database.EnsureCreated();
}

await host.RunAsync();