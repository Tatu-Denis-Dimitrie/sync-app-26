using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncApp26.Infrastructure.Migrations
{
    /// <summary>Schema V4 fingerprint column. Nullable: existing rows are V1-V3.</summary>
    public partial class AddDocumentContentHashSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentContentHashSnapshot",
                table: "SignatureRecords",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentContentHashSnapshot",
                table: "SignatureRecords");
        }
    }
}
