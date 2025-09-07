using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TerminBot.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingCodeToAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookingCode",
                table: "Appointments",
                type: "TEXT",
                nullable: true);

            
            migrationBuilder.Sql(@"
        UPDATE ""Appointments""
        SET ""BookingCode"" = substr(replace(upper(hex(randomblob(8))), '0', 'Z'), 1, 4) || '-' ||
                              substr(replace(upper(hex(randomblob(8))), '0', 'Z'), 1, 4)
        WHERE ""BookingCode"" IS NULL;
    ");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BookingCode",
                table: "Appointments",
                column: "BookingCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_BookingCode",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "BookingCode",
                table: "Appointments");
        }

    }
}
