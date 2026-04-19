using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualLibrary.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOlLastModified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stores the last_modified timestamp returned by OpenLibrary so that
            // the bulk import service can skip re-writing records that haven't changed.
            migrationBuilder.AddColumn<DateTime>(
                name: "OlLastModified",
                table: "Works",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OlLastModified",
                table: "Editions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OlLastModified",
                table: "Authors",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OlLastModified", table: "Works");
            migrationBuilder.DropColumn(name: "OlLastModified", table: "Editions");
            migrationBuilder.DropColumn(name: "OlLastModified", table: "Authors");
        }
    }
}
