using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VereinMember.Data;

#nullable disable

namespace VereinMember.Data.Migrations;

[DbContext(typeof(MembershipDbContext))]
[Migration("20261117183000_AddNonMemberGuardianFields")]
public partial class AddNonMemberGuardianFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "GuardianFirstName",
            schema: "Membership",
            table: "Member",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GuardianLastName",
            schema: "Membership",
            table: "Member",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GuardianEmail",
            schema: "Membership",
            table: "Member",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GuardianPhone",
            schema: "Membership",
            table: "Member",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "GuardianFirstName",
            schema: "Membership",
            table: "Member");

        migrationBuilder.DropColumn(
            name: "GuardianLastName",
            schema: "Membership",
            table: "Member");

        migrationBuilder.DropColumn(
            name: "GuardianEmail",
            schema: "Membership",
            table: "Member");

        migrationBuilder.DropColumn(
            name: "GuardianPhone",
            schema: "Membership",
            table: "Member");
    }
}
