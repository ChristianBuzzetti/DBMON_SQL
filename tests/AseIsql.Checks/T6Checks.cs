using System.Data;
using System.Globalization;
using AseIsql;

internal static class T6Checks
{
    private static DataTable Table(params string[] columns)
    {
        var table = new DataTable(); foreach (string column in columns) table.Columns.Add(column, typeof(object)); return table;
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static DdlTableMetadata Definition()
    {
        var header = Table("name", "owner", "lock_scheme"); header.Rows.Add("orders]", "sales", "datarows");
        var columns = Table("ordinal", "name", "type", "type_owner", "is_user_type", "length", "precision", "scale", "nullable", "identity", "status2", "default_id", "default_owner", "default_name", "rule_id", "rule_owner", "rule_name");
        columns.Rows.Add(1, "id", "numeric", "dbo", 0, 9, 18, 0, 0, 1, 0, 0, DBNull.Value, DBNull.Value, 0, DBNull.Value, DBNull.Value);
        columns.Rows.Add(2, "amount", "decimal", "dbo", 0, 9, 12, 2, 0, 0, 0, 50, DBNull.Value, DBNull.Value, 0, DBNull.Value, DBNull.Value);
        columns.Rows.Add(3, "label", "univarchar", "dbo", 0, 40, 0, 0, 1, 0, 0, 51, "sales", "empty_label", 52, "sales", "label_rule");
        columns.Rows.Add(4, "code", "code_type", "sales", 1, 8, 0, 0, 1, 0, 0, 0, DBNull.Value, DBNull.Value, 0, DBNull.Value, DBNull.Value);
        var indexes = Table("index_name", "key_ordinal", "key_count", "column_name", "sort_order", "clustered", "is_unique", "primary_key", "unique_constraint");
        indexes.Rows.Add("pk_orders", 1, 1, "id", "ASC", 1, 1, 1, 0);
        // Deliberately reversed: the builder must use ordinals, rather than result row order.
        indexes.Rows.Add("ix_amount", 2, 2, "id", "ASC", 0, 0, 0, 0);
        indexes.Rows.Add("ix_amount", 1, 2, "amount", "DESC", 0, 0, 0, 0);
        indexes.Rows.Add("uq_code", 1, 1, "code", "ASC", 0, 1, 0, 1);
        var foreign = Table("constraint_name", "key_ordinal", "key_count", "column_name", "ref_database", "ref_owner", "ref_table", "ref_column", "match_full");
        foreign.Rows.Add("fk_code", 1, 1, "code", "lookup db", "reference", "codes", "code", 1);
        var text = Table("id", "text");
        text.Rows.Add(50, "DEFAULT (0.00)"); text.Rows.Add(51, "create default sales.empty_label as "); text.Rows.Add(51, "'n/a'");
        text.Rows.Add(52, "create rule sales.label_rule as @value <> ''"); text.Rows.Add(53, "CHECK (amount >= 0)");
        var checks = Table("id", "name"); checks.Rows.Add(53, "ck_amount");
        return new(header, columns, indexes, foreign, text, checks);
    }
    internal static void Run(Action<string, Action> check, Action<bool> assert)
    {
        check("ASE DDL preserves column types, identity, defaults/rules, keys, sort order, locks and cross-database FKs", () => {
            var oldCulture = CultureInfo.CurrentCulture;
            try {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");
                var metadata = Definition();
                string ddl = DdlGenerator.Table("app db", "sales", "orders]", metadata);
                assert(ddl.Contains("USE [app db]\nGO") && ddl.Contains("CREATE TABLE [sales].[orders]]]"));
                assert(ddl.Contains("[id] numeric(18,0) IDENTITY NOT NULL") && ddl.Contains("[amount] decimal(12,2) DEFAULT (0.00) NOT NULL"));
                assert(ddl.Contains("[label] univarchar(20) NULL") && ddl.Contains("[code] [code_type] NULL"));
                assert(ddl.Contains(") LOCK datarows") && ddl.Contains("PRIMARY KEY CLUSTERED ([id] ASC)"));
                assert(ddl.Contains("CREATE NONCLUSTERED INDEX [ix_amount] ON [sales].[orders]]] ([amount] DESC, [id] ASC)"));
                assert(ddl.Contains("ADD CONSTRAINT [uq_code] UNIQUE NONCLUSTERED ([code] ASC)"));
                assert(ddl.Contains("REFERENCES [lookup db].[reference].[codes] ([code]) MATCH FULL"));
                assert(ddl.Contains("ADD CONSTRAINT [ck_amount] CHECK (amount >= 0)"));
                assert(ddl.Contains("CREATE DEFAULT [sales].[empty_label] AS 'n/a'") && ddl.Contains("exec sp_bindefault '[sales].[empty_label]', '[sales].[orders]]].[label]'"));
                assert(ddl.Contains("exec sp_bindrule '[sales].[label_rule]'"));
                assert(metadata.Columns.Rows.Count == 4 && metadata.Text.Rows.Count == 5 && metadata.IndexKeys.Rows[1]["key_ordinal"].Equals(2));
            } finally { CultureInfo.CurrentCulture = oldCulture; }
        });
        check("Live ODBC captions drive DDL, RS and IQ parsing when ColumnName is column_N", () => {
            static DataTable DriverShape(DataTable table) {
                var captions = table.Columns.Cast<DataColumn>().Select(column => column.Caption).ToArray();
                for (int i = 0; i < captions.Length; i++) { table.Columns[i].ColumnName = "column_" + i.ToString(CultureInfo.InvariantCulture); table.Columns[i].Caption = captions[i]; }
                return table;
            }
            var metadata = Definition();
            foreach (var table in new[] { metadata.Header, metadata.Columns, metadata.IndexKeys, metadata.ForeignKeys, metadata.Text, metadata.Checks }) DriverShape(table);
            assert(DdlGenerator.Table("app", "sales", "orders]", metadata).Contains("PRIMARY KEY CLUSTERED ([id] ASC)"));
            var threads = Table("Spid", "State", "Name"); threads.Rows.Add(1, "DOWN", "DSI");
            assert((bool)RsMonitor.Threads(DriverShape(threads)).Rows[0]["Needs attention"]);
            var disk = Table("Total Segs", "Used Segs"); disk.Rows.Add(4, 1);
            assert(RsMonitor.DiskSpace(DriverShape(disk)).Rows[0]["Usage %"].Equals(25m));
            var status = Table("Name", "Value"); status.Rows.Add("Database", "test");
            assert(IqMetrics.Status(DriverShape(status)).Rows[0]["Key"].Equals("Database"));
            var spaces = Table("Usage"); spaces.Rows.Add("37.5%");
            assert(IqMetrics.Dbspaces(DriverShape(spaces)).Rows[0]["Usage %"].Equals(37.5m));
            var transactions = Table("State", "TempWorkSpaceKB"); transactions.Rows.Add("active", 5);
            assert(IqMetrics.TransactionSummary(DriverShape(transactions)).Rows[0]["Temp workspace KB"].Equals(5m));
        });
        check("ASE DDL refuses missing text, partial keys, computed/encrypted columns and stale table identity", () => {
            var metadata = Definition(); metadata.Text.Rows.Clear();
            Reject<FormatException>(() => DdlGenerator.Table("app", "sales", "orders]", metadata));
            metadata = Definition(); metadata.IndexKeys.Rows.RemoveAt(1);
            Reject<FormatException>(() => DdlGenerator.Table("app", "sales", "orders]", metadata));
            foreach (int flag in new[] { 16, 32, 64, 128 }) {
                metadata = Definition(); metadata.Columns.Rows[0]["status2"] = flag;
                Reject<FormatException>(() => DdlGenerator.Table("app", "sales", "orders]", metadata));
            }
            metadata = Definition(); metadata.Header.Rows[0]["name"] = "renamed";
            Reject<FormatException>(() => DdlGenerator.Table("app", "sales", "orders]", metadata));
            metadata = Definition(); metadata.ForeignKeys.Rows[0]["ref_owner"] = DBNull.Value;
            Reject<FormatException>(() => DdlGenerator.Table("app", "sales", "orders]", metadata));
            metadata = Definition(); metadata.ForeignKeys.Rows[0]["key_count"] = 2;
            Reject<FormatException>(() => DdlGenerator.Table("app", "sales", "orders]", metadata));
        });
        check("ASE index scripting distinguishes unique indexes from unique constraints and rejects bad sort/ordinals", () => {
            var keys = Definition().IndexKeys.Clone(); keys.Rows.Add("ix_unique", 1, 1, "label", "DESC", 1, 1, 0, 0);
            assert(DdlGenerator.Index("app", "sales", "orders", keys).Contains("CREATE UNIQUE CLUSTERED INDEX [ix_unique] ON [sales].[orders] ([label] DESC)"));
            keys.Rows[0]["sort_order"] = "sideways";
            Reject<FormatException>(() => DdlGenerator.Index("app", "sales", "orders", keys));
            keys.Rows[0]["sort_order"] = "ASC"; keys.Rows[0]["key_ordinal"] = 2;
            Reject<FormatException>(() => DdlGenerator.Index("app", "sales", "orders", keys));
            keys.Rows[0]["key_ordinal"] = 1; keys.Columns.Add("function_based", typeof(int)); keys.Rows[0]["function_based"] = 1;
            Reject<FormatException>(() => DdlGenerator.Index("app", "sales", "orders", keys));
            keys.Rows.Clear(); Reject<FormatException>(() => DdlGenerator.Index("app", "sales", "orders", keys));
        });
        check("ASE type DDL uses sp_addtype, preserves decimal shape and declares bound-object prerequisites", () => {
            var type = Table("owner", "name", "type", "is_user_type", "length", "precision", "scale", "nullable", "identity", "default_id", "default_owner", "default_name", "rule_id");
            type.Rows.Add("sales", "money's", "decimal", 0, 9, 12, 3, 1, 0, 50, "sales", "zero", 0);
            string ddl = DdlGenerator.UserType("app", type);
            assert(ddl.Contains("exec sp_addtype 'money''s', 'decimal(12,3)', 'null'"));
            assert(ddl.Contains("exec sp_bindefault '[sales].[zero]', 'money''s'"));
            type.Rows[0]["scale"] = 13; Reject<FormatException>(() => DdlGenerator.UserType("app", type));
            var unicode = Definition().Columns.Rows[2]; unicode["length"] = 3;
            Reject<FormatException>(() => DdlGenerator.DataType(unicode));
            unicode.Table.Columns.Add("national_char_size", typeof(int)); unicode["national_char_size"] = 3;
            unicode["type"] = "nchar"; unicode["length"] = 30;
            assert(DdlGenerator.DataType(unicode) == "nchar(10)");
            unicode["length"] = 31; Reject<FormatException>(() => DdlGenerator.DataType(unicode));
        });
        check("ASE stored source preserves fragment whitespace and refuses unavailable definitions", () => {
            var source = Table("text"); source.Rows.Add("create procedure dbo.p as select "); source.Rows.Add("1\n");
            assert(DdlGenerator.Code("app", source).Contains("select 1\n"));
            source.Rows.Add(DBNull.Value); Reject<FormatException>(() => DdlGenerator.Code("app", source));
            source.Rows.Clear(); Reject<FormatException>(() => DdlGenerator.Code("app", source));
        });
        check("ASE metadata SQL quotes database/name values, orders fragments and expands composite keys", () => {
            string sql = CatalogSql.AseTableDdl("a]b", "o", "t'1", 42);
            assert(sql.Contains("[a]]b]..syscolumns") && sql.Contains("x.number, x.colid2, x.colid"));
            assert(sql.Contains("i.keycnt - case when i.indid = 1 then 0 else 1 end"));
            assert(sql.Contains("r.fokey16") && sql.Contains("r.refkey16") && sql.Contains("object_owner_id"));
            assert(CatalogSql.AseIndexKeys("db", "o", "t", 42, "ix'1").Contains("i.name = 'ix''1'"));
            assert(CatalogSql.AseTableDetails("db", 42).Contains("row_count(db_id('db'), o.id)"));
            Reject<ArgumentException>(() => CatalogSql.AseTableDdl("db", "o", "t", 0));
        });
        check("RS thread parser preserves raw fields, normalizes headings and marks DOWN/suspended", () => {
            var source = Table(" Spid ", " State ", "Name", "Info");
            source.Rows.Add(1, "Awaiting Command", "DSI", "a.db"); source.Rows.Add(2, "DOWN", "DSI", "b.db");
            source.Rows.Add(3, "Suspended", "REP AGENT", "c.db"); source.Rows.Add(4, DBNull.Value, "DSI", "d.db");
            var threads = RsMonitor.Threads(source);
            assert(!(bool)threads.Rows[0]["Needs attention"] && (bool)threads.Rows[1]["Needs attention"] && (bool)threads.Rows[2]["Needs attention"]);
            assert(!(bool)threads.Rows[3]["Needs attention"] && source.Columns.Count == 4 && threads.Rows[2]["Info"].Equals("c.db"));
            Reject<FormatException>(() => RsMonitor.Threads(Table("unknown")));
        });
        check("RS disk usage is invariant, typed and unknown for zero/null/invalid capacity", () => {
            var source = Table("Partition", "Total_Segs", "Used Segs");
            source.Rows.Add("p1", 20, 3); source.Rows.Add("p2", 0, 0); source.Rows.Add("p3", DBNull.Value, 1);
            source.Rows.Add("p4", 10, 11); source.Rows.Add("p5", 10, -1); source.Rows.Add("p6", "12.5", "3.125");
            var result = RsMonitor.DiskSpace(source);
            assert(result.Rows[0]["Usage %"].Equals(15m) && result.Rows[5]["Usage %"].Equals(25m));
            assert(result.Rows.Cast<DataRow>().Skip(1).Take(4).All(row => row["Usage %"] is DBNull));
            assert(source.Columns.Count == 3); Reject<FormatException>(() => RsMonitor.DiskSpace(Table("Partition")));
        });
        check("RS connection actions reject RCL injection and participate in PROD state detection", () => {
            string suspend = RsMonitor.ConnectionCommand(true, "ASE_1", "sales");
            string resume = RsMonitor.ConnectionCommand(false, "ASE_1", "sales");
            assert(suspend == "suspend connection to ASE_1.sales" && resume == "resume connection to ASE_1.sales");
            assert(SqlRisk.Review(suspend).ChangesState && SqlRisk.Review(resume).ChangesState);
            foreach (string invalid in new[] { "", "srv.db", "a\nshutdown", "a;shutdown", "'quoted'", "a--", " a" })
                Reject<ArgumentException>(() => RsMonitor.ConnectionCommand(true, invalid, "db"));
            Reject<ArgumentException>(() => RsMonitor.ConnectionCommand(false, "srv", "db\ngo\nshutdown"));
        });
        check("IQ status preserves NULL and duplicate keys; dbspace percentages tolerate server headings", () => {
            var status = Table("Name", "Value"); status.Rows.Add("Database", "test"); status.Rows.Add("Database", DBNull.Value);
            var result = IqMetrics.Status(status); assert(result.Rows.Count == 2 && result.Rows[1]["Value"] is DBNull);
            var positional = Table("c1", "c2"); positional.Rows.Add("Page size", "128k"); assert(IqMetrics.Status(positional).Rows.Count == 1);
            Reject<FormatException>(() => IqMetrics.Status(Table("one")));
            var spaces = Table("DBSpaceName", "Usage"); spaces.Rows.Add("main", " 73.5% "); spaces.Rows.Add("temp", "unknown"); spaces.Rows.Add("bad", "101%");
            var parsed = IqMetrics.Dbspaces(spaces);
            assert(parsed.Rows[0]["Usage %"].Equals(73.5m) && parsed.Rows[1]["Usage %"] is DBNull && parsed.Rows[2]["Usage %"] is DBNull);
            var blocks = Table("UsedBlocks", "TotalBlocks"); blocks.Rows.Add(1, 4); blocks.Rows.Add(0, 0);
            assert(IqMetrics.Dbspaces(blocks).Rows[0]["Usage %"].Equals(25m) && IqMetrics.Dbspaces(blocks).Rows[1]["Usage %"] is DBNull);
        });
        check("IQ connections stay raw and transaction summary counts states with honest workspace totals", () => {
            var connections = Table("ConnHandle", "Userid"); connections.Rows.Add(12, "DBA");
            assert(IqMetrics.Connections(connections).Rows[0]["Userid"].Equals("DBA"));
            Reject<FormatException>(() => IqMetrics.Connections(Table("unexpected")));
            var transactions = Table("State", "TempWorkSpaceKB", "TxnID");
            transactions.Rows.Add("ACTIVE", 12, 1); transactions.Rows.Add("active ", 8, 2); transactions.Rows.Add("COMMITTED", DBNull.Value, 3);
            var summary = IqMetrics.TransactionSummary(transactions);
            assert(summary.Rows.Count == 2 && summary.Rows[0]["Transactions"].Equals(2) && summary.Rows[0]["Temp workspace KB"].Equals(20m));
            assert(summary.Rows[1]["Temp workspace KB"] is DBNull && transactions.Rows.Count == 3);
            transactions.Rows.Clear(); assert(IqMetrics.TransactionSummary(transactions).Rows.Count == 0);
            Reject<FormatException>(() => IqMetrics.TransactionSummary(Table("unexpected")));
        });
    }
}
