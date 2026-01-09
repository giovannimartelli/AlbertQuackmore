using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExpenseTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class TagSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Expenses");

            migrationBuilder.AddColumn<int>(
                name: "TagId",
                table: "Expenses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SubCategoryId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tags_SubCategories_SubCategoryId",
                        column: x => x.SubCategoryId,
                        principalTable: "SubCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TagId",
                table: "Expenses",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_SubCategoryId",
                table: "Tags",
                column: "SubCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Tags_TagId",
                table: "Expenses",
                column: "TagId",
                principalTable: "Tags",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Tags_TagId",
                table: "Expenses");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_TagId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "TagId",
                table: "Expenses");

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Expenses",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
