using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Sabemi.Pagamentos.Api.Middleware;
using Sabemi.Pagamentos.Api.Webhooks;
using Sabemi.Pagamentos.Application;
using Sabemi.Pagamentos.Infrastructure;
using Sabemi.Pagamentos.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

const string PoliticaCors = "painel";

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums saem como texto: o painel exibe "Processado", nao 3.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Sabemi — Webhooks de Pagamento",
        Version = "v1",
        Description = "Recebimento idempotente de notificacoes de pagamento com processamento assincrono "
                      + "e consultas para o painel administrativo.",
    });

    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = AutenticacaoWebhookFilter.HeaderApiKey,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Chave do parceiro. O endpoint de webhook tambem exige o header "
                      + AutenticacaoWebhookFilter.HeaderAssinatura + " quando a assinatura esta habilitada.",
    });

    var arquivoXml = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var caminhoXml = Path.Combine(AppContext.BaseDirectory, arquivoXml);
    if (File.Exists(caminhoXml))
    {
        options.IncludeXmlComments(caminhoXml);
    }
});

builder.Services.Configure<OpcoesWebhook>(builder.Configuration.GetSection(OpcoesWebhook.Secao));
builder.Services.AddScoped<AutenticacaoWebhookFilter>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

// Em producao o painel e servido pelo mesmo host (nginx faz proxy), entao o CORS
// existe apenas para o servidor de desenvolvimento do Vite.
builder.Services.AddCors(options => options.AddPolicy(PoliticaCors, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origens").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors(PoliticaCors);

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Habilitado"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Sabemi Webhooks v1");
        options.DocumentTitle = "Sabemi — Webhooks de Pagamento";
        options.RoutePrefix = "swagger";
    });
}

app.MapControllers();
app.MapHealthChecks("/health");

await PrepararBancoAsync(app);

await app.RunAsync();

// Migrations no startup facilitam o docker compose; em producao esse passo
// normalmente vive no pipeline de deploy, nao no processo da API.
static async Task PrepararBancoAsync(WebApplication app)
{
    if (!app.Configuration.GetValue("Database:MigrarNoStartup", defaultValue: false))
    {
        return;
    }

    using var escopo = app.Services.CreateScope();
    var contexto = escopo.ServiceProvider.GetRequiredService<AppDbContext>();

    await contexto.Database.MigrateAsync();
}

/// <summary>Exposto para o WebApplicationFactory dos testes de integracao.</summary>
public partial class Program;
