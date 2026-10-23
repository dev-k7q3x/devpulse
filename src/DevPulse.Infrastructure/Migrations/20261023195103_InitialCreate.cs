using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DevPulse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    github_id = table.Column<long>(type: "bigint", nullable: false),
                    login = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                    avatar_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    access_token_encrypted = table.Column<string>(type: "text", nullable: false),
                    token_scopes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    token_invalid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    token_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "digests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    digest_date = table.Column<DateOnly>(type: "date", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    recipient_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    html_body = table.Column<string>(type: "text", nullable: true),
                    ai_used = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ai_model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ai_input_tokens = table.Column<int>(type: "integer", nullable: true),
                    ai_output_tokens = table.Column<int>(type: "integer", nullable: true),
                    email_provider_message_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    attempt_count = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    priorities = table.Column<string>(type: "jsonb", nullable: true),
                    repository_summary = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_digests", x => x.id);
                    table.CheckConstraint("ck_digests_sent_at", "(status = 'Sent') = (sent_at IS NOT NULL)");
                    table.CheckConstraint("ck_digests_status", "status IN ('Pending', 'Sent', 'Failed')");
                    table.ForeignKey(
                        name: "fk_digests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tracked_repositories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    github_repo_id = table.Column<long>(type: "bigint", nullable: false),
                    github_node_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    full_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    html_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    default_branch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_private = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracked_repositories", x => x.id);
                    table.ForeignKey(
                        name: "fk_tracked_repositories_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    pending_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    pending_email_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    pending_email_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    send_hour = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)7),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "UTC"),
                    digest_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_settings", x => x.user_id);
                    table.CheckConstraint("ck_user_settings_pending_email_token", "(pending_email IS NULL) = (pending_email_token_hash IS NULL)");
                    table.CheckConstraint("ck_user_settings_send_hour", "send_hour BETWEEN 0 AND 23");
                    table.ForeignKey(
                        name: "fk_user_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repository_snapshots",
                columns: table => new
                {
                    tracked_repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    open_pr_count = table.Column<int>(type: "integer", nullable: false),
                    open_issue_count = table.Column<int>(type: "integer", nullable: false),
                    ci_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ci_commit_sha = table.Column<string>(type: "character(40)", fixedLength: true, maxLength: 40, nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refreshed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    open_pull_requests = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repository_snapshots", x => x.tracked_repository_id);
                    table.CheckConstraint("ck_repository_snapshots_ci_status", "ci_status IN ('None', 'Success', 'Failure', 'Pending')");
                    table.CheckConstraint("ck_repository_snapshots_open_issue_count", "open_issue_count >= 0");
                    table.CheckConstraint("ck_repository_snapshots_open_pr_count", "open_pr_count >= 0");
                    table.CheckConstraint("ck_repository_snapshots_open_pull_requests", "open_pull_requests IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_repository_snapshots_tracked_repositories_tracked_repositor",
                        column: x => x.tracked_repository_id,
                        principalTable: "tracked_repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_digests_created_at",
                table: "digests",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ux_digests_user_id_digest_date",
                table: "digests",
                columns: new[] { "user_id", "digest_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tracked_repositories_user_id_github_repo_id",
                table: "tracked_repositories",
                columns: new[] { "user_id", "github_repo_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_user_settings_pending_email_token_hash",
                table: "user_settings",
                column: "pending_email_token_hash",
                unique: true,
                filter: "pending_email_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_users_github_id",
                table: "users",
                column: "github_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "digests");

            migrationBuilder.DropTable(
                name: "repository_snapshots");

            migrationBuilder.DropTable(
                name: "user_settings");

            migrationBuilder.DropTable(
                name: "tracked_repositories");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
