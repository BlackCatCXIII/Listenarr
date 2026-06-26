using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCoverSidecarSettingsToApplicationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExportCoverSidecars",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CoverSidecarFileName",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "cover.jpg");

            migrationBuilder.AddColumn<bool>(
                name: "OverwriteManagedCoverSidecars",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExportCoverSidecars",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "CoverSidecarFileName",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "OverwriteManagedCoverSidecars",
                table: "ApplicationSettings");
        }
    }
}
