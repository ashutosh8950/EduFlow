using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecurityAndStatusIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM dbo.AspNetUsers
    WHERE NormalizedEmail IS NOT NULL
    GROUP BY NormalizedEmail
    HAVING COUNT(*) > 1)
BEGIN
    THROW 51000, 'Cannot create unique normalized-email index: duplicate normalized email values exist.', 1;
END");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApplicationStatusHistory_FromStatus",
                table: "ApplicationStatusHistory",
                sql: "[FromStatus] IS NULL OR [FromStatus] IN ('Draft', 'Submitted', 'UnderReview', 'Approved', 'Rejected', 'Withdrawn')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApplicationStatusHistory_FromStatus",
                table: "ApplicationStatusHistory");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");
        }
    }
}
