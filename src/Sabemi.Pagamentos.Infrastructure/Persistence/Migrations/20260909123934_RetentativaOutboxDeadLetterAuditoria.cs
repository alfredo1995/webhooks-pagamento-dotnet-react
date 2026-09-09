using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sabemi.Pagamentos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetentativaOutboxDeadLetterAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProximaTentativaEmUtc",
                table: "EventosWebhook",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReivindicadoEmUtc",
                table: "EventosWebhook",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeadLetters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdTransacao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IdContrato = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReprocessadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReprocessadoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeadLetters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MensagensOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TraceParent = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TraceState = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CriadaEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublicadaEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    ProximaTentativaEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UltimoErro = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReservaToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReservadaAteUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MensagensOutbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegistrosAuditoria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Papel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Metodo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Recurso = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Consulta = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IpOrigem = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StatusHttp = table.Column<int>(type: "int", nullable: false),
                    TraceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrosAuditoria", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosWebhook_Status_ProximaTentativa",
                table: "EventosWebhook",
                columns: new[] { "Status", "ProximaTentativaEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetters_EventoId",
                table: "DeadLetters",
                column: "EventoId");

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetters_ReprocessadoEmUtc",
                table: "DeadLetters",
                column: "ReprocessadoEmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MensagensOutbox_EventoId",
                table: "MensagensOutbox",
                column: "EventoId");

            migrationBuilder.CreateIndex(
                name: "IX_MensagensOutbox_Pendentes",
                table: "MensagensOutbox",
                columns: new[] { "PublicadaEmUtc", "ProximaTentativaEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MensagensOutbox_ReservaToken",
                table: "MensagensOutbox",
                column: "ReservaToken");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosAuditoria_EmUtc",
                table: "RegistrosAuditoria",
                column: "EmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosAuditoria_Usuario",
                table: "RegistrosAuditoria",
                column: "Usuario");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeadLetters");

            migrationBuilder.DropTable(
                name: "MensagensOutbox");

            migrationBuilder.DropTable(
                name: "RegistrosAuditoria");

            migrationBuilder.DropIndex(
                name: "IX_EventosWebhook_Status_ProximaTentativa",
                table: "EventosWebhook");

            migrationBuilder.DropColumn(
                name: "ProximaTentativaEmUtc",
                table: "EventosWebhook");

            migrationBuilder.DropColumn(
                name: "ReivindicadoEmUtc",
                table: "EventosWebhook");
        }
    }
}
