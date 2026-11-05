using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OEESentinel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "downtime_reasons",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    category = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_downtime_reasons", x => x.code);
                    table.CheckConstraint("chk_downtime_reasons_category", "category IN ('PLANNED','UNPLANNED')");
                    table.CheckConstraint("chk_downtime_reasons_code_upper", "code = UPPER(code)");
                });

            migrationBuilder.CreateTable(
                name: "machines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ideal_cycle_seconds = table.Column<double>(type: "double precision", nullable: false),
                    planned_shift_seconds = table.Column<double>(type: "double precision", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_machines", x => x.id);
                    table.UniqueConstraint("AK_machines_code", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "log_ingestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rows_processed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    rows_rejected = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    rejected_sample = table.Column<string>(type: "jsonb", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_log_ingestions", x => x.id);
                    table.CheckConstraint("chk_log_ingestions_completed_at", "(status = 'COMPLETED' AND completed_at IS NOT NULL) OR (status <> 'COMPLETED' AND completed_at IS NULL)");
                    table.CheckConstraint("chk_log_ingestions_failure_reason", "(status = 'FAILED' AND failure_reason IS NOT NULL) OR (status <> 'FAILED' AND failure_reason IS NULL)");
                    table.CheckConstraint("chk_log_ingestions_rows", "rows_processed >= 0 AND rows_rejected >= 0 AND rows_rejected <= rows_processed");
                    table.CheckConstraint("chk_log_ingestions_status", "status IN ('PENDING','PROCESSING','COMPLETED','FAILED')");
                    table.ForeignKey(
                        name: "fk_log_ingestions_machine",
                        column: x => x.machine_code,
                        principalTable: "machines",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ingestion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_line_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_events", x => x.id);
                    table.CheckConstraint("chk_events_line", "source_line_number > 0");
                    table.CheckConstraint("chk_events_quantity", "quantity >= 0");
                    table.ForeignKey(
                        name: "fk_events_ingestion",
                        column: x => x.ingestion_id,
                        principalTable: "log_ingestions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "production_shifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_parts = table.Column<int>(type: "integer", nullable: false),
                    approved_parts = table.Column<int>(type: "integer", nullable: false),
                    downtime_breakdown = table.Column<string>(type: "jsonb", nullable: false),
                    availability = table.Column<double>(type: "numeric(5,4)", nullable: false),
                    performance = table.Column<double>(type: "numeric(5,4)", nullable: false),
                    quality = table.Column<double>(type: "numeric(5,4)", nullable: false),
                    oee = table.Column<double>(type: "numeric(5,4)", nullable: false),
                    source_ingestion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    superseded_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_shifts", x => x.id);
                    table.CheckConstraint("chk_shifts_availability", "availability BETWEEN 0 AND 1");
                    table.CheckConstraint("chk_shifts_no_self_supersede", "superseded_by_id IS NULL OR superseded_by_id <> id");
                    table.CheckConstraint("chk_shifts_oee", "oee BETWEEN 0 AND 1");
                    table.CheckConstraint("chk_shifts_performance", "performance BETWEEN 0 AND 1");
                    table.CheckConstraint("chk_shifts_quality", "quality BETWEEN 0 AND 1");
                    table.CheckConstraint("chk_shifts_totals", "total_parts >= 0 AND approved_parts >= 0 AND approved_parts <= total_parts");
                    table.CheckConstraint("chk_shifts_window", "window_start < window_end");
                    table.ForeignKey(
                        name: "fk_shifts_ingestion",
                        column: x => x.source_ingestion_id,
                        principalTable: "log_ingestions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shifts_machine",
                        column: x => x.machine_code,
                        principalTable: "machines",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shifts_superseded_by",
                        column: x => x.superseded_by_id,
                        principalTable: "production_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_downtime_reasons_code_ci",
                table: "downtime_reasons",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_log_ingestions_machine_code",
                table: "log_ingestions",
                column: "machine_code");

            migrationBuilder.CreateIndex(
                name: "IX_machines_code",
                table: "machines",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_production_events_ingestion_recorded",
                table: "production_events",
                columns: new[] { "ingestion_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "idx_production_shifts_ingestion",
                table: "production_shifts",
                column: "source_ingestion_id");

            migrationBuilder.CreateIndex(
                name: "IX_production_shifts_machine_code",
                table: "production_shifts",
                column: "machine_code");

            migrationBuilder.CreateIndex(
                name: "IX_production_shifts_superseded_by_id",
                table: "production_shifts",
                column: "superseded_by_id");

            // 1) rejected_parts as generated column
            migrationBuilder.Sql(@"
                ALTER TABLE production_shifts
                ADD COLUMN rejected_parts INTEGER
                    GENERATED ALWAYS AS (total_parts - approved_parts) STORED;
            ");

            // 2) JSONB validation function for downtime_breakdown column
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_validate_downtime_breakdown(p_json JSONB)
                RETURNS BOOLEAN AS $$
                DECLARE
                    v_key TEXT;
                    v_val JSONB;
                BEGIN
                    IF p_json IS NULL THEN RETURN FALSE; END IF;
                    IF jsonb_typeof(p_json) <> 'object' THEN RETURN FALSE; END IF;
                    FOR v_key, v_val IN SELECT * FROM jsonb_each(p_json) LOOP
                        IF jsonb_typeof(v_val) <> 'number' THEN RETURN FALSE; END IF;
                        IF (v_val)::text::numeric < 0 THEN RETURN FALSE; END IF;
                    END LOOP;
                    RETURN TRUE;
                END;
                $$ LANGUAGE plpgsql IMMUTABLE;
            ");

            // 3) CHECK shape
            migrationBuilder.Sql(@"
                ALTER TABLE production_shifts
                ADD CONSTRAINT chk_shifts_downtime_shape
                CHECK (fn_validate_downtime_breakdown(downtime_breakdown));
            ");

            // 4) "one current shift per window"
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX uq_production_shifts_current
                ON production_shifts (machine_code, window_start, window_end)
                WHERE superseded_by_id IS NULL;
            ");

            // 5) partial index reading
            migrationBuilder.Sql(@"
                CREATE INDEX idx_production_shifts_machine_window
                ON production_shifts (machine_code, window_start, window_end)
                WHERE superseded_by_id IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_production_shifts_current;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_production_shifts_machine_window;");
            migrationBuilder.Sql("ALTER TABLE production_shifts DROP CONSTRAINT IF EXISTS chk_shifts_downtime_shape;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_validate_downtime_breakdown(JSONB);");
            migrationBuilder.Sql("ALTER TABLE production_shifts DROP COLUMN IF EXISTS rejected_parts;");

            migrationBuilder.DropTable(
                name: "downtime_reasons");

            migrationBuilder.DropTable(
                name: "production_events");

            migrationBuilder.DropTable(
                name: "production_shifts");

            migrationBuilder.DropTable(
                name: "log_ingestions");

            migrationBuilder.DropTable(
                name: "machines");
        }
    }
}
