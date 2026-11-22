using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoCoReTrade.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NotificationWorkflowReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SupportTicketId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WithdrawalRequestId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SupportTicketId",
                table: "Notifications",
                column: "SupportTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_WithdrawalRequestId",
                table: "Notifications",
                column: "WithdrawalRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_SupportTickets_SupportTicketId",
                table: "Notifications",
                column: "SupportTicketId",
                principalTable: "SupportTickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_WithdrawalRequests_WithdrawalRequestId",
                table: "Notifications",
                column: "WithdrawalRequestId",
                principalTable: "WithdrawalRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_SupportTickets_SupportTicketId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_WithdrawalRequests_WithdrawalRequestId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_SupportTicketId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_WithdrawalRequestId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "SupportTicketId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "WithdrawalRequestId",
                table: "Notifications");
        }
    }
}
