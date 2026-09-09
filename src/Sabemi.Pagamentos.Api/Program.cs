using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Sabemi.Pagamentos.Api.Auditoria;
using Sabemi.Pagamentos.Api.Middleware;
using Sabemi.Pagamentos.Api.Observabilidade;
using Sabemi.Pagamentos.Api.Seguranca;
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

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Token do painel, obtido em POST /api/auth/login.",
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
        }] = [],
    });

    var arquivoXml = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var caminhoXml = Path.Combine(AppContext.BaseDirectory, arquivoXml);
    if (File.Exists(caminhoXml))
    {
        options.IncludeXmlComments(caminhoXml);
    }
});

builder.Services.Configure<OpcoesWebhook>(builder.Configuration.GetSection(OpcoesWebhook.Secao));
builder.Services.Configure<OpcoesAutenticacao>(builder.Configuration.GetSection(OpcoesAutenticacao.Secao));
builder.Services.Configure<OpcoesAuditoria>(builder.Configuration.GetSection(OpcoesAuditoria.Secao));
builder.Services.AddScoped<AutenticacaoWebhookFilter>();
builder.Services.AddSingleton<IServicoTokenJwt, ServicoTokenJwt>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddObservabilidade(builder.Configuration);
builder.Services.AddRateLimitingDeWebhook();

ConfigurarAutenticacao(builder);

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

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Depois da autorizacao: a trilha precisa da identidade resolvida e do status
// final da resposta, inclusive quando ela e 401 ou 403 — tentativa recusada
// tambem e informacao de auditoria.
app.UseMiddleware<AuditoriaMiddleware>();

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

// Com Autenticacao:Authority preenchido, a API apenas valida tokens do IdP
// corporativo — o modo de producao. Sem ele, valida os tokens que ela mesma
// emitiu com a chave simetrica, que e o que permite demonstrar o painel sem um
// IdP no ar.
static void ConfigurarAutenticacao(WebApplicationBuilder builder)
{
    var opcoes = builder.Configuration.GetSection(OpcoesAutenticacao.Secao).Get<OpcoesAutenticacao>()
                 ?? new OpcoesAutenticacao();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(jwt =>
        {
            jwt.MapInboundClaims = false;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = opcoes.EmissaoLocalHabilitada ? opcoes.Emissor : opcoes.Authority,
                ValidAudience = opcoes.Audiencia,
                NameClaimType = OpcoesAutenticacao.ClaimUsuario,
                RoleClaimType = OpcoesAutenticacao.ClaimPapel,
                ClockSkew = TimeSpan.FromSeconds(30),
            };

            if (opcoes.EmissaoLocalHabilitada)
            {
                if (string.IsNullOrWhiteSpace(opcoes.ChaveAssinatura) || opcoes.ChaveAssinatura.Length < 32)
                {
                    throw new InvalidOperationException(
                        "Autenticacao:ChaveAssinatura precisa ter ao menos 32 caracteres para assinar em HS256.");
                }

                jwt.TokenValidationParameters.IssuerSigningKey =
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opcoes.ChaveAssinatura));
            }
            else
            {
                jwt.Authority = opcoes.Authority;
                jwt.RequireHttpsMetadata = opcoes.ExigirHttpsNoMetadata;
            }
        });

    builder.Services.AddAuthorization();
}

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
