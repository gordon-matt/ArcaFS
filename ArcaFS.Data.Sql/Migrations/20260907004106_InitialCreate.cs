using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcaFS.Data.Sql.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "app");

        migrationBuilder.CreateTable(
            name: "AccessGroups",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                IsExternallyManaged = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AccessGroups", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ActivityEvents",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Kind = table.Column<int>(type: "int", nullable: false),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                SourceId = table.Column<int>(type: "int", nullable: true),
                Path = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: true),
                TargetPath = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: true),
                ShareToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                Succeeded = table.Column<bool>(type: "bit", nullable: false),
                Detail = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ActivityEvents", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ApiTokens",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                TokenPrefix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsRevoked = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ApiTokens", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Roles",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Roles", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ServerSettings",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ServerSettings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Sources",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                IsDisabled = table.Column<bool>(type: "bit", nullable: false),
                IsReadOnly = table.Column<bool>(type: "bit", nullable: false),
                DenyByDefault = table.Column<bool>(type: "bit", nullable: false),
                DefaultPermissions = table.Column<int>(type: "int", nullable: false),
                AdministratorsBypassRules = table.Column<bool>(type: "bit", nullable: false),
                DefaultUserScope = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                CreateUserDirectory = table.Column<bool>(type: "bit", nullable: false),
                IsVisibleInSidebar = table.Column<bool>(type: "bit", nullable: false),
                IgnoreHiddenFiles = table.Column<bool>(type: "bit", nullable: false),
                FollowSymlinks = table.Column<bool>(type: "bit", nullable: false),
                IsIndexed = table.Column<bool>(type: "bit", nullable: false),
                IndexIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                ExcludePatterns = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                LastIndexedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IndexedItemCount = table.Column<long>(type: "bigint", nullable: true),
                IndexedSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                SortOrder = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Sources", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Users",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CanCreateShares = table.Column<bool>(type: "bit", nullable: false),
                CanUseApi = table.Column<bool>(type: "bit", nullable: false),
                PreferredTheme = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                PreferredViewMode = table.Column<int>(type: "int", nullable: false),
                ShowHiddenFiles = table.Column<bool>(type: "bit", nullable: false),
                SingleClickToOpen = table.Column<bool>(type: "bit", nullable: false),
                UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                AccessFailedCount = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Users", x => x.Id));

        migrationBuilder.CreateTable(
            name: "RoleClaims",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoleClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_RoleClaims_Roles_RoleId",
                    column: x => x.RoleId,
                    principalSchema: "app",
                    principalTable: "Roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AccessRules",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SourceId = table.Column<int>(type: "int", nullable: false),
                Path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                SubjectType = table.Column<int>(type: "int", nullable: false),
                SubjectId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                Effect = table.Column<int>(type: "int", nullable: false),
                Permissions = table.Column<int>(type: "int", nullable: false),
                Propagation = table.Column<int>(type: "int", nullable: false),
                Priority = table.Column<int>(type: "int", nullable: false),
                Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccessRules", x => x.Id);
                table.ForeignKey(
                    name: "FK_AccessRules_Sources_SourceId",
                    column: x => x.SourceId,
                    principalSchema: "app",
                    principalTable: "Sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "FavoriteItems",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                SourceId = table.Column<int>(type: "int", nullable: false),
                Path = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: false),
                Label = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                IsDirectory = table.Column<bool>(type: "bit", nullable: false),
                SortOrder = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FavoriteItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_FavoriteItems_Sources_SourceId",
                    column: x => x.SourceId,
                    principalSchema: "app",
                    principalTable: "Sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "IndexedItems",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SourceId = table.Column<int>(type: "int", nullable: false),
                Path = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: false),
                ParentPath = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: false),
                Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                NameLower = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                Extension = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                IsDirectory = table.Column<bool>(type: "bit", nullable: false),
                IsHidden = table.Column<bool>(type: "bit", nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                Depth = table.Column<int>(type: "int", nullable: false),
                ChildCount = table.Column<int>(type: "int", nullable: false),
                ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IndexedItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_IndexedItems_Sources_SourceId",
                    column: x => x.SourceId,
                    principalSchema: "app",
                    principalTable: "Sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Shares",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                SourceId = table.Column<int>(type: "int", nullable: false),
                Path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                Mode = table.Column<int>(type: "int", nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                MaxDownloads = table.Column<int>(type: "int", nullable: true),
                DownloadCount = table.Column<int>(type: "int", nullable: false),
                DisableThumbnails = table.Column<bool>(type: "bit", nullable: false),
                DisableFileViewer = table.Column<bool>(type: "bit", nullable: false),
                Theme = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                LastAccessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDisabled = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Shares", x => x.Id);
                table.ForeignKey(
                    name: "FK_Shares_Sources_SourceId",
                    column: x => x.SourceId,
                    principalSchema: "app",
                    principalTable: "Sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AccessGroupMembers",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                AccessGroupId = table.Column<int>(type: "int", nullable: false),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                AddedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccessGroupMembers", x => x.Id);
                table.ForeignKey(
                    name: "FK_AccessGroupMembers_AccessGroups_AccessGroupId",
                    column: x => x.AccessGroupId,
                    principalSchema: "app",
                    principalTable: "AccessGroups",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AccessGroupMembers_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "app",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserClaims",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserClaims_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "app",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserLogins",
            schema: "app",
            columns: table => new
            {
                LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey(
                    name: "FK_UserLogins_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "app",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserRoles",
            schema: "app",
            columns: table => new
            {
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                table.ForeignKey(
                    name: "FK_UserRoles_Roles_RoleId",
                    column: x => x.RoleId,
                    principalSchema: "app",
                    principalTable: "Roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_UserRoles_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "app",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserTokens",
            schema: "app",
            columns: table => new
            {
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey(
                    name: "FK_UserTokens_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "app",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AccessGroupMembers_AccessGroupId_UserId",
            schema: "app",
            table: "AccessGroupMembers",
            columns: new[] { "AccessGroupId", "UserId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AccessGroupMembers_UserId",
            schema: "app",
            table: "AccessGroupMembers",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_AccessGroups_Name",
            schema: "app",
            table: "AccessGroups",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AccessRules_SourceId_Path",
            schema: "app",
            table: "AccessRules",
            columns: new[] { "SourceId", "Path" });

        migrationBuilder.CreateIndex(
            name: "IX_AccessRules_SubjectType_SubjectId",
            schema: "app",
            table: "AccessRules",
            columns: new[] { "SubjectType", "SubjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_OccurredAt",
            schema: "app",
            table: "ActivityEvents",
            column: "OccurredAt");

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_SourceId_OccurredAt",
            schema: "app",
            table: "ActivityEvents",
            columns: new[] { "SourceId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_UserId_OccurredAt",
            schema: "app",
            table: "ActivityEvents",
            columns: new[] { "UserId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ApiTokens_TokenHash",
            schema: "app",
            table: "ApiTokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ApiTokens_UserId",
            schema: "app",
            table: "ApiTokens",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_FavoriteItems_SourceId",
            schema: "app",
            table: "FavoriteItems",
            column: "SourceId");

        migrationBuilder.CreateIndex(
            name: "IX_FavoriteItems_UserId_SourceId_Path",
            schema: "app",
            table: "FavoriteItems",
            columns: new[] { "UserId", "SourceId", "Path" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IndexedItems_ContentHash",
            schema: "app",
            table: "IndexedItems",
            column: "ContentHash");

        migrationBuilder.CreateIndex(
            name: "IX_IndexedItems_SourceId_Kind",
            schema: "app",
            table: "IndexedItems",
            columns: new[] { "SourceId", "Kind" });

        migrationBuilder.CreateIndex(
            name: "IX_IndexedItems_SourceId_NameLower",
            schema: "app",
            table: "IndexedItems",
            columns: new[] { "SourceId", "NameLower" });

        migrationBuilder.CreateIndex(
            name: "IX_IndexedItems_SourceId_ParentPath",
            schema: "app",
            table: "IndexedItems",
            columns: new[] { "SourceId", "ParentPath" });

        migrationBuilder.CreateIndex(
            name: "IX_IndexedItems_SourceId_Path",
            schema: "app",
            table: "IndexedItems",
            columns: new[] { "SourceId", "Path" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IndexedItems_SourceId_SizeBytes",
            schema: "app",
            table: "IndexedItems",
            columns: new[] { "SourceId", "SizeBytes" });

        migrationBuilder.CreateIndex(
            name: "IX_RoleClaims_RoleId",
            schema: "app",
            table: "RoleClaims",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "RoleNameIndex",
            schema: "app",
            table: "Roles",
            column: "NormalizedName",
            unique: true,
            filter: "[NormalizedName] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_ServerSettings_Key",
            schema: "app",
            table: "ServerSettings",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Shares_OwnerUserId",
            schema: "app",
            table: "Shares",
            column: "OwnerUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Shares_SourceId",
            schema: "app",
            table: "Shares",
            column: "SourceId");

        migrationBuilder.CreateIndex(
            name: "IX_Shares_Token",
            schema: "app",
            table: "Shares",
            column: "Token",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Sources_Name",
            schema: "app",
            table: "Sources",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserClaims_UserId",
            schema: "app",
            table: "UserClaims",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserLogins_UserId",
            schema: "app",
            table: "UserLogins",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserRoles_RoleId",
            schema: "app",
            table: "UserRoles",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            schema: "app",
            table: "Users",
            column: "NormalizedEmail");

        migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            schema: "app",
            table: "Users",
            column: "NormalizedUserName",
            unique: true,
            filter: "[NormalizedUserName] IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AccessGroupMembers",
            schema: "app");

        migrationBuilder.DropTable(
            name: "AccessRules",
            schema: "app");

        migrationBuilder.DropTable(
            name: "ActivityEvents",
            schema: "app");

        migrationBuilder.DropTable(
            name: "ApiTokens",
            schema: "app");

        migrationBuilder.DropTable(
            name: "FavoriteItems",
            schema: "app");

        migrationBuilder.DropTable(
            name: "IndexedItems",
            schema: "app");

        migrationBuilder.DropTable(
            name: "RoleClaims",
            schema: "app");

        migrationBuilder.DropTable(
            name: "ServerSettings",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Shares",
            schema: "app");

        migrationBuilder.DropTable(
            name: "UserClaims",
            schema: "app");

        migrationBuilder.DropTable(
            name: "UserLogins",
            schema: "app");

        migrationBuilder.DropTable(
            name: "UserRoles",
            schema: "app");

        migrationBuilder.DropTable(
            name: "UserTokens",
            schema: "app");

        migrationBuilder.DropTable(
            name: "AccessGroups",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Sources",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Roles",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Users",
            schema: "app");
    }
}
