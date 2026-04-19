using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualLibrary.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReadRecordsAndIsOwned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add IsOwned column (default false).
            migrationBuilder.AddColumn<bool>(
                name: "IsOwned",
                table: "UserBooks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 2. Migrate existing "Owned" rows: mark them as owned, reset reading
            //    status to WantToRead (owning a book doesn't imply having read it).
            migrationBuilder.Sql("""
                UPDATE "UserBooks"
                SET "IsOwned" = TRUE,
                    "Status"  = 'WantToRead'
                WHERE "Status" = 'Owned';
                """);

            // 3. Create ReadRecords table.
            migrationBuilder.CreateTable(
                name: "ReadRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserBookId = table.Column<Guid>(type: "uuid", nullable: false),
                    DateRead = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadRecords_UserBooks_UserBookId",
                        column: x => x.UserBookId,
                        principalTable: "UserBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadRecords_UserBookId",
                table: "ReadRecords",
                column: "UserBookId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ReadRecords");

            // Restore the "Owned" status for books that were migrated.
            migrationBuilder.Sql("""
                UPDATE "UserBooks"
                SET "Status" = 'Owned'
                WHERE "IsOwned" = TRUE;
                """);

            migrationBuilder.DropColumn(name: "IsOwned", table: "UserBooks");
        }
    }
}
