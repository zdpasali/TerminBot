using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TerminBot.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Appointments_UserId_DayIso_TimeIso",
                table: "Appointments",
                columns: new[] { "UserId", "DayIso", "TimeIso" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_UserId_DayIso_TimeIso",
                table: "Appointments"
            );
        }
    }
}
