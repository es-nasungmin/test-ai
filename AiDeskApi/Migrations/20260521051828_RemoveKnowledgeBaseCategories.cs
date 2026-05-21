using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiDeskApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveKnowledgeBaseCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryLarge",
                table: "KnowledgeBases");

            migrationBuilder.DropColumn(
                name: "CategoryMedium",
                table: "KnowledgeBases");

            migrationBuilder.DropColumn(
                name: "CategorySmall",
                table: "KnowledgeBases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryLarge",
                table: "KnowledgeBases",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryMedium",
                table: "KnowledgeBases",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategorySmall",
                table: "KnowledgeBases",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }
    }
}
