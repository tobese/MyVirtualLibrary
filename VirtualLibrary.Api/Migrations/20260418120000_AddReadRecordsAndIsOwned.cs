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
            // No-op: folded into InitialCreate (20260417143047_InitialCreate.cs).
            // Kept so existing databases that already have this migration ID in
            // __EFMigrationsHistory can continue running without errors.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: see Up().
        }
    }
}
