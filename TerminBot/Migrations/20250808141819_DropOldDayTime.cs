using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TerminBot.Migrations
{
    /// <inheritdoc />
    public partial class DropOldDayTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Day", table: "Appointments");
            migrationBuilder.DropColumn(name: "Time", table: "Appointments");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
