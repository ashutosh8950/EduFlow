using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdmissionApplicationUniqueStudentCourseIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_AdmissionApplications_StudentUserId_CourseId",
                table: "AdmissionApplications",
                columns: new[] { "StudentUserId", "CourseId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_AdmissionApplications_StudentUserId_CourseId",
                table: "AdmissionApplications");
        }
    }
}
