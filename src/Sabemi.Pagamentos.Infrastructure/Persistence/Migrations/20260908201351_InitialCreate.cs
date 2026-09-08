using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sabemi.Pagamentos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventosWebhook",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTransacao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PayloadBruto = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrigemParceiro = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecebidoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IdContrato = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DataPagamento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusPagamento = table.Column<int>(type: "int", nullable: true),
                    MotivoFalha = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    ProcessadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DuracaoProcessamentoMs = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosWebhook", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StatusContratos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdContrato = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValorTotalPago = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ValorTotalEstornado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    QuantidadePagamentos = table.Column<int>(type: "int", nullable: false),
                    QuantidadeEstornos = table.Column<int>(type: "int", nullable: false),
                    UltimoStatus = table.Column<int>(type: "int", nullable: false),
                    UltimaTransacao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UltimoPagamentoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatusContratos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosWebhook_IdContrato",
                table: "EventosWebhook",
                column: "IdContrato");

            migrationBuilder.CreateIndex(
                name: "IX_EventosWebhook_RecebidoEmUtc",
                table: "EventosWebhook",
                column: "RecebidoEmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EventosWebhook_Status",
                table: "EventosWebhook",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_EventosWebhook_IdTransacao",
                table: "EventosWebhook",
                column: "IdTransacao",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_StatusContratos_IdContrato",
                table: "StatusContratos",
                column: "IdContrato",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosWebhook");

            migrationBuilder.DropTable(
                name: "StatusContratos");
        }
    }
}
