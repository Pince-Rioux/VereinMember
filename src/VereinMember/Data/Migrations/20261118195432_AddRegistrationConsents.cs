using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VereinMember.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationConsents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PrivacyConsentIp",
                schema: "Membership",
                table: "Member",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrivacyConsentTextVersion",
                schema: "Membership",
                table: "Member",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SepaMandateConsentedAt",
                schema: "Membership",
                table: "Member",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SepaMandateTextVersion",
                schema: "Membership",
                table: "Member",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrivacyConsentTextVersion",
                schema: "Membership",
                table: "Member");

            migrationBuilder.DropColumn(
                name: "SepaMandateConsentedAt",
                schema: "Membership",
                table: "Member");

            migrationBuilder.DropColumn(
                name: "SepaMandateTextVersion",
                schema: "Membership",
                table: "Member");

            migrationBuilder.AlterColumn<string>(
                name: "PrivacyConsentIp",
                schema: "Membership",
                table: "Member",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(45)",
                oldMaxLength: 45,
                oldNullable: true);
        }
    }
}
