using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReadLog.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveReadEntryNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "ReadEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "ReadEntries",
                type: "TEXT",
                nullable: true);
        }
    }
}
