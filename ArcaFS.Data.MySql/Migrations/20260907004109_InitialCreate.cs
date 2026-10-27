using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ArcaFS.Data.MySql.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "AccessGroups",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                Description = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                IsExternallyManaged = table.Column<bool>(type: "tinyint(1)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AccessGroups", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "ActivityEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                Kind = table.Column<int>(type: "int", nullable: false),
                UserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                UserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                SourceId = table.Column<int>(type: "int", nullable: true),
                Path = table.Column<string>(type: "varchar(700)", maxLength: 700, nullable: true),
                TargetPath = table.Column<string>(type: "varchar(700)", maxLength: 700, nullable: true),
                ShareToken = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                Succeeded = table.Column<bool>(type: "tinyint(1)", nullable: false),
                Detail = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                IpAddress = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                OccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ActivityEvents", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "ApiTokens",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                UserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                TokenHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                TokenPrefix = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                LastUsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                IsRevoked = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ApiTokens", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "Roles",
            columns: table => new
            {
                Id = table.Column<string>(type: "varchar(255)", nullable: false),
                Description = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                NormalizedName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Roles", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "ServerSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                Key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                Value = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ServerSettings", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "Sources",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                Path = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                Description = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                IsReadOnly = table.Column<bool>(type: "tinyint(1)", nullable: false),
                DenyByDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                DefaultPermissions = table.Column<int>(type: "int", nullable: false),
                AdministratorsBypassRules = table.Column<bool>(type: "tinyint(1)", nullable: false),
                DefaultUserScope = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                CreateUserDirectory = table.Column<bool>(type: "tinyint(1)", nullable: false),
                IsVisibleInSidebar = table.Column<bool>(type: "tinyint(1)", nullable: false),
                IgnoreHiddenFiles = table.Column<bool>(type: "tinyint(1)", nullable: false),
                FollowSymlinks = table.Column<bool>(type: "tinyint(1)", nullable: false),
                IsIndexed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                IndexIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                ExcludePatterns = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                LastIndexedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                IndexedItemCount = table.Column<long>(type: "bigint", nullable: true),
                IndexedSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                SortOrder = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Sources", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<string>(type: "varchar(255)", nullable: false),
                DisplayName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CanCreateShares = table.Column<bool>(type: "tinyint(1)", nullable: false),
                CanUseApi = table.Column<bool>(type: "tinyint(1)", nullable: false),
                PreferredTheme = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                PreferredViewMode = table.Column<int>(type: "int", nullable: false),
                ShowHiddenFiles = table.Column<bool>(type: "tinyint(1)", nullable: false),
                SingleClickToOpen = table.Column<bool>(type: "tinyint(1)", nullable: false),
                UserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                PasswordHash = table.Column<string>(type: "longtext", nullable: true),
                SecurityStamp = table.Column<string>(type: "longtext", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true),
                PhoneNumber = table.Column<string>(type: "longtext", nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                LockoutEnd = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                LockoutEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                AccessFailedCount = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Users", x => x.Id))
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "RoleClaims",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                RoleId = table.Column<string>(type: "varchar(255)", nullable: false),
                ClaimType = table.Column<string>(type: "longtext", nullable: true),
                ClaimValue = table.Column<string>(type: "longtext", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoleClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_RoleClaims_Roles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "Roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "AccessRules",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                SourceId = table.Column<int>(type: "int", nullable: false),
                Path = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                SubjectType = table.Column<int>(type: "int", nullable: false),
                SubjectId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true),
                Effect = table.Column<int>(type: "int", nullable: false),
                Permissions = table.Column<int>(type: "int", nullable: false),
                Propagation = table.Column<int>(type: "int", nullable: false),
                Priority = table.Column<int>(type: "int", nullable: false),
                Description = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                CreatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccessRules", x => x.Id);
                table.ForeignKey(
                    name: "FK_AccessRules_Sources_SourceId",
                    column: x => x.SourceId,
                    principalTable: "Sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "FavoriteItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                UserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                SourceId = table.Column<int>(type: "int", nullable: false),
                Path = table.Column<string>(type: "varchar(700)", maxLength: 700, nullable: false),
                Label = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                IsDirectory = table.Column<bool>(type: "tinyint(1)", nullable: false),
                SortOrder = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FavoriteItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_FavoriteItems_Sources_SourceId",
                    column: x => x.SourceId,
                    principalTable: "Sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "IndexedItems",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                SourceId = table.Column<int>(type: "int", nullable: false),
                Path = table.Column<string>(type: "varchar(700)", maxLength: 700, nullable: false),
                ParentPath = table.Column<string>(type: "varchar(700)", maxLength: 700, nullable: false),
                Name = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                NameLower = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                Extension = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                IsDirectory = table.Column<bool>(type: "tinyint(1)", nullable: false),
                IsHidden = table.Column<bool>(type: "tinyint(1)", nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                Depth = table.Column<int>(type: "int", nullable: false),
                ChildCount = table.Column<int>(type: "int", nullable: false),
                ContentHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IndexedItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_IndexedItems_Sources_SourceId",
                    column: x => x.SourceId,
                    principalTable: "Sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "Shares",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                Token = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                SourceId = table.Column<int>(type: "int", nullable: false),
                Path = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                OwnerUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                Title = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                Description = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                Mode = table.Column<int>(type: "int", nullable: false),
                PasswordHash = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                MaxDownloads = table.Column<int>(type: "int", nullable: true),
                DownloadCount = table.Column<int>(type: "int", nullable: false),
                DisableThumbnails = table.Column<bool>(type: "tinyint(1)", nullable: false),
                DisableFileViewer = table.Column<bool>(type: "tinyint(1)", nullable: false),
                Theme = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                LastAccessedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Shares", x => x.Id);
                table.ForeignKey(
                    name: "FK_Shares_Sources_SourceId",
                    column: x => x.SourceId,
                    principalTable: "Sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "AccessGroupMembers",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                AccessGroupId = table.Column<int>(type: "int", nullable: false),
                UserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false),
                AddedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccessGroupMembers", x => x.Id);
                table.ForeignKey(
                    name: "FK_AccessGroupMembers_AccessGroups_AccessGroupId",
                    column: x => x.AccessGroupId,
                    principalTable: "AccessGroups",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AccessGroupMembers_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "UserClaims",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                UserId = table.Column<string>(type: "varchar(255)", nullable: false),
                ClaimType = table.Column<string>(type: "longtext", nullable: true),
                ClaimValue = table.Column<string>(type: "longtext", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserClaims_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "UserLogins",
            columns: table => new
            {
                LoginProvider = table.Column<string>(type: "varchar(255)", nullable: false),
                ProviderKey = table.Column<string>(type: "varchar(255)", nullable: false),
                ProviderDisplayName = table.Column<string>(type: "longtext", nullable: true),
                UserId = table.Column<string>(type: "varchar(255)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey(
                    name: "FK_UserLogins_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "UserRoles",
            columns: table => new
            {
                UserId = table.Column<string>(type: "varchar(255)", nullable: false),
                RoleId = table.Column<string>(type: "varchar(255)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                table.ForeignKey(
                    name: "FK_UserRoles_Roles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "Roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_UserRoles_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "UserTokens",
            columns: table => new
            {
                UserId = table.Column<string>(type: "varchar(255)", nullable: false),
                LoginProvider = table.Column<string>(type: "varchar(255)", nullable: false),
                Name = table.Column<string>(type: "varchar(255)", nullable: false),
                Value = table.Column<string>(type: "longtext", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey(
                    name: "FK_UserTokens_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_AccessGroupMembers_AccessGroupId_UserId",
            table: "AccessGroupMembers",
            columns: new[] { "AccessGroupId", "UserId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AccessGroupMembers_UserId",
            table: "AccessGroupMembers",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_AccessGroups_Name",
            table: "AccessGroups",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AccessRules_SourceId_Path",
            table: "AccessRules",
            columns: new[] { "SourceId", "Path" });

        migrationBuilder.CreateIndex(
            name: "IX_AccessRules_SubjectType_SubjectId",
            table: "AccessRules",
            columns: new[] { "SubjectType", "SubjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_OccurredAt",
            table: "ActivityEvents",
            column: "OccurredAt");

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_SourceId_OccurredAt",
            table: "ActivityEvents",
            columns: new[] { "SourceId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_UserId_OccurredAt",
            table: "ActivityEvents",
            columns: new[] { "UserId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ApiTokens_TokenHash",
            table: "ApiTokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ApiTokens_UserId",
            table: "ApiTokens",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_FavoriteItems_SourceId",
            table: "FavoriteItems",
            column: "SourceId");

        migrationBuilder.CreateIndex(
            name: "IX_FavoriteItems_UserId_SourceId_Path",
            table: "FavoriteItems",
            columns: new[] { "UserId", "SourceId", "Path" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IndexedItems_ContentHash",
            table: "IndexedItems",
            column: "ContentHash");

        migrationBuilder.CreateIndex(
            name: "IX_IndexedItems_SourceId_Kind",
            table: "IndexedItems",
            columns: new[] { "SourceId", "Kind" });

        migrationBuilder.CreateIndex(
            name: "IX_IndexedItems_SourceId_NameLower",
            table: "IndexedItems",
            columns: new[] { "SourceId", "NameLower" });

        migrationBuilder.CreateIndex(
            name: "IX_IndexedItems_SourceId_ParentPath",
            table: "IndexedItems",
            columns: new[] { "SourceId", "ParentPath" });

        migrationBuilder.CreateIndex(
            name: "IX_IndexedItems_SourceId_Path",
            table: "IndexedItems",
            columns: new[] { "SourceId", "Path" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IndexedItems_SourceId_SizeBytes",
            table: "IndexedItems",
            columns: new[] { "SourceId", "SizeBytes" });

        migrationBuilder.CreateIndex(
            name: "IX_RoleClaims_RoleId",
            table: "RoleClaims",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "RoleNameIndex",
            table: "Roles",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ServerSettings_Key",
            table: "ServerSettings",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Shares_OwnerUserId",
            table: "Shares",
            column: "OwnerUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Shares_SourceId",
            table: "Shares",
            column: "SourceId");

        migrationBuilder.CreateIndex(
            name: "IX_Shares_Token",
            table: "Shares",
            column: "Token",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Sources_Name",
            table: "Sources",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserClaims_UserId",
            table: "UserClaims",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserLogins_UserId",
            table: "UserLogins",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserRoles_RoleId",
            table: "UserRoles",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            table: "Users",
            column: "NormalizedEmail");

        migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            table: "Users",
            column: "NormalizedUserName",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AccessGroupMembers");

        migrationBuilder.DropTable(
            name: "AccessRules");

        migrationBuilder.DropTable(
            name: "ActivityEvents");

        migrationBuilder.DropTable(
            name: "ApiTokens");

        migrationBuilder.DropTable(
            name: "FavoriteItems");

        migrationBuilder.DropTable(
            name: "IndexedItems");

        migrationBuilder.DropTable(
            name: "RoleClaims");

        migrationBuilder.DropTable(
            name: "ServerSettings");

        migrationBuilder.DropTable(
            name: "Shares");

        migrationBuilder.DropTable(
            name: "UserClaims");

        migrationBuilder.DropTable(
            name: "UserLogins");

        migrationBuilder.DropTable(
            name: "UserRoles");

        migrationBuilder.DropTable(
            name: "UserTokens");

        migrationBuilder.DropTable(
            name: "AccessGroups");

        migrationBuilder.DropTable(
            name: "Sources");

        migrationBuilder.DropTable(
            name: "Roles");

        migrationBuilder.DropTable(
            name: "Users");
    }
}
