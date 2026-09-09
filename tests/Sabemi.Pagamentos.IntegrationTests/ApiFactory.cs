using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sabemi.Pagamentos.Api.Seguranca;
using Sabemi.Pagamentos.Application.Webhooks;
using Sabemi.Pagamentos.Infrastructure.Persistence;

namespace Sabemi.Pagamentos.IntegrationTests;

/// <summary>
/// Sobe a API inteira em memoria — pipeline, filtros, autenticacao, DI, EF Core,
/// o outbox e os workers de background reais — trocando apenas o provider por SQLite.
/// </summary>
/// <remarks>
/// <para>
/// O banco usa <c>mode=memory&amp;cache=shared</c> com um nome unico por fabrica:
/// cada classe de teste tem seu proprio banco isolado, e ainda assim varias
/// conexoes (requisicao HTTP e workers) enxergam os mesmos dados. Uma conexao
/// "guardia" fica aberta porque o SQLite descarta o banco em memoria quando a
/// ultima conexao fecha.
/// </para>
/// <para>
/// O atraso simulado vai a zero e a concorrencia a um: os testes verificam o
/// comportamento assincrono, nao a duracao dele, e serializar evita disputa de
/// escrita no SQLite. O orcamento de retentativas cai para uma tentativa, de modo
/// que uma falha de processamento chega ao desfecho na hora; a jornada completa
/// de retentativa ate a DLQ tem uma fabrica propria em
/// <see cref="RetentativaEDeadLetterTests"/>.
/// </para>
/// </remarks>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "chave-de-teste";
    public const string Segredo = "segredo-de-teste";
    public const string Parceiro = "banco-de-teste";

    public const string Administrador = "admin-teste";
    public const string Operador = "operador-teste";
    public const string SenhaPainel = "senha-de-teste";

    private const string ChaveJwt = "chave-de-teste-para-assinar-jwt-com-mais-de-32-caracteres";

    private readonly string _nomeBanco = $"sabemi-testes-{Guid.NewGuid():N}";
    private SqliteConnection? _guardia;

    private string ConnectionString => $"DataSource=file:{_nomeBanco}?mode=memory&cache=shared";

    /// <summary>Cliente sem token, para exercitar o 401 dos endpoints do painel.</summary>
    public HttpClient CriarClienteAnonimo() => CreateDefaultClient();

    /// <summary>Cliente autenticado com o papel informado.</summary>
    public HttpClient CriarClienteDoPainel(string login)
    {
        var cliente = CreateDefaultClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", EmitirToken(login));

        return cliente;
    }

    /// <summary>Monta o corpo assinado como o banco parceiro faria.</summary>
    public static HttpRequestMessage MontarRequisicao(
        string corpoJson,
        string? apiKey = ApiKey,
        string? assinatura = null,
        string segredo = Segredo)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, "/webhooks/pagamento")
        {
            Content = new StringContent(corpoJson, Encoding.UTF8),
        };

        requisicao.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        if (apiKey is not null)
        {
            requisicao.Headers.Add("X-Api-Key", apiKey);
        }

        requisicao.Headers.Add("X-Signature", assinatura ?? CalculadoraAssinatura.Calcular(corpoJson, segredo));

        return requisicao;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Development para que uma falha inesperada apareca com stack trace no
        // ProblemDetails, encurtando o diagnostico quando um teste quebra.
        builder.UseEnvironment("Development");

        builder.UseSetting("Database:MigrarNoStartup", "false");
        builder.UseSetting("Processamento:AtrasoSimuladoMs", "0");
        builder.UseSetting("Processamento:Concorrencia", "1");
        builder.UseSetting("Processamento:Fila", "Memoria");
        builder.UseSetting("Processamento:Outbox:IntervaloMs", "50");
        builder.UseSetting("Processamento:IntervaloSupervisorSegundos", "5");
        builder.UseSetting("Processamento:LeaseProcessamentoSegundos", "30");
        builder.UseSetting("Processamento:Retentativa:MaximoTentativas", "1");
        builder.UseSetting("Processamento:Retentativa:JitterPercentual", "0");

        // O provider e trocado por SQLite logo abaixo, mas AddInfrastructure le a
        // connection string na composicao do container e recusa string vazia — e
        // vazia e o que o appsettings versionado traz, de proposito. Um valor
        // qualquer aqui satisfaz a checagem sem nunca ser usado para abrir conexao.
        builder.UseSetting("ConnectionStrings:SqlServer", "Server=trocado-por-sqlite;Database=testes");

        builder.UseSetting("Webhook:ExigirAssinatura", "true");
        builder.UseSetting("Webhook:Parceiros:0:Nome", Parceiro);
        builder.UseSetting("Webhook:Parceiros:0:ApiKey", ApiKey);
        builder.UseSetting("Webhook:Parceiros:0:Segredo", Segredo);
        builder.UseSetting("Webhook:Parceiros:0:LimitePorMinuto", "1000");

        builder.UseSetting("Autenticacao:Authority", string.Empty);
        builder.UseSetting("Autenticacao:ChaveAssinatura", ChaveJwt);
        builder.UseSetting("Autenticacao:Usuarios:0:Login", Administrador);
        builder.UseSetting("Autenticacao:Usuarios:0:Senha", SenhaPainel);
        builder.UseSetting("Autenticacao:Usuarios:0:Papel", OpcoesAutenticacao.PapelAdministrador);
        builder.UseSetting("Autenticacao:Usuarios:1:Login", Operador);
        builder.UseSetting("Autenticacao:Usuarios:1:Senha", SenhaPainel);
        builder.UseSetting("Autenticacao:Usuarios:1:Papel", OpcoesAutenticacao.PapelOperador);

        Ajustar(builder);

        builder.ConfigureServices(services =>
        {
            RemoverTodos(services, typeof(DbContextOptions<AppDbContext>));
            RemoverTodos(services, typeof(DbContextOptions));
            RemoverTodos(services, typeof(AppDbContext));

            _guardia = new SqliteConnection(ConnectionString);
            _guardia.Open();

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(ConnectionString));

            using var provedor = services.BuildServiceProvider();
            using var escopo = provedor.CreateScope();
            escopo.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        });
    }

    /// <summary>Ponto de extensao para uma classe de teste que precisa de outra configuracao.</summary>
    protected virtual void Ajustar(IWebHostBuilder builder)
    {
    }

    /// <summary>
    /// Emite o token pelo proprio servico da API.
    /// </summary>
    /// <remarks>
    /// Passar pelo <c>POST /api/auth/login</c> exigiria uma chamada assincrona no
    /// construtor da fixture. O caminho HTTP do login tem teste proprio; aqui o
    /// que interessa e ter um token valido para exercitar os endpoints do painel.
    /// </remarks>
    private string EmitirToken(string login)
    {
        var servico = Services.GetRequiredService<IServicoTokenJwt>();

        return servico.Autenticar(login, SenhaPainel)?.Token
               ?? throw new InvalidOperationException($"Nao foi possivel emitir token para '{login}'.");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _guardia?.Dispose();
            _guardia = null;
        }
    }

    private static void RemoverTodos(IServiceCollection services, Type tipo)
    {
        foreach (var descritor in services.Where(d => d.ServiceType == tipo).ToList())
        {
            services.Remove(descritor);
        }
    }
}
