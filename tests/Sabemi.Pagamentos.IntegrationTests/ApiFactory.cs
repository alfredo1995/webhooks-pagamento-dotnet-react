using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sabemi.Pagamentos.Application.Webhooks;
using Sabemi.Pagamentos.Infrastructure.Persistence;

namespace Sabemi.Pagamentos.IntegrationTests;

/// <summary>
/// Sobe a API inteira em memoria — pipeline, filtros, DI, EF Core e o worker de
/// background reais — trocando apenas o provider por SQLite.
/// </summary>
/// <remarks>
/// <para>
/// O banco usa <c>mode=memory&amp;cache=shared</c> com um nome unico por fabrica:
/// cada classe de teste tem seu proprio banco isolado, e ainda assim varias
/// conexoes (requisicao HTTP e worker) enxergam os mesmos dados. Uma conexao
/// "guardia" fica aberta porque o SQLite descarta o banco em memoria quando a
/// ultima conexao fecha.
/// </para>
/// <para>
/// O atraso simulado vai a zero e a concorrencia a um: os testes verificam o
/// comportamento assincrono, nao a duracao dele, e serializar evita disputa de
/// escrita no SQLite.
/// </para>
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "chave-de-teste";
    public const string Segredo = "segredo-de-teste";
    public const string Parceiro = "banco-de-teste";

    private readonly string _nomeBanco = $"sabemi-testes-{Guid.NewGuid():N}";
    private SqliteConnection? _guardia;

    private string ConnectionString => $"DataSource=file:{_nomeBanco}?mode=memory&cache=shared";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Development para que uma falha inesperada apareca com stack trace no
        // ProblemDetails, encurtando o diagnostico quando um teste quebra.
        builder.UseEnvironment("Development");

        builder.UseSetting("Database:MigrarNoStartup", "false");
        builder.UseSetting("Processamento:AtrasoSimuladoMs", "0");
        builder.UseSetting("Processamento:Concorrencia", "1");
        builder.UseSetting("Webhook:ExigirAssinatura", "true");
        builder.UseSetting("Webhook:Parceiros:0:Nome", Parceiro);
        builder.UseSetting("Webhook:Parceiros:0:ApiKey", ApiKey);
        builder.UseSetting("Webhook:Parceiros:0:Segredo", Segredo);

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

    /// <summary>Cliente ja configurado com ApiKey; a assinatura vai por requisicao.</summary>
    public HttpClient CriarClienteAutenticado()
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        return cliente;
    }

    /// <summary>Monta o corpo assinado como o banco parceiro faria.</summary>
    public static HttpRequestMessage MontarRequisicao(string corpoJson, string? apiKey = ApiKey, string? assinatura = null)
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

        requisicao.Headers.Add("X-Signature", assinatura ?? CalculadoraAssinatura.Calcular(corpoJson, Segredo));

        return requisicao;
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
