using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NBXplorer.Migrations
{
    public partial class init : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GenericTables",
                columns: table => new
                {
                    PartitionKeyRowKey = table.Column<string>(nullable: false),
                    Value = table.Column<byte[]>(nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenericTables", x => x.PartitionKeyRowKey);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GenericTables");
        }
    }
}
