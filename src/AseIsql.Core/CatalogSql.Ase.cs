using System.Globalization;

namespace AseIsql;

public static partial class CatalogSql
{
    private static string DbId(string database) => "db_id(" + ResultFormats.Literal(database) + ")";

    public static string AseTableDetails(string database, int id)
    {
        string db = Db(database), number = Id(id), dbid = DbId(database);
        return $"select o.name, row_count({dbid}, o.id) as row_count_estimate, reserved_pages({dbid}, o.id) as reserved_pages, used_pages({dbid}, o.id) as used_pages, convert(numeric(20,0), reserved_pages({dbid}, o.id)) * @@maxpagesize / 1024 as reserved_kb, convert(numeric(20,0), used_pages({dbid}, o.id)) * @@maxpagesize / 1024 as used_kb, @@maxpagesize as page_bytes, lockscheme(o.id, {dbid}) as lock_scheme from {db}sysobjects o where o.id = {number};\n" +
            $"select p.name as partition_name, p.partitionid, p.indid, case when p.indid in (0,1) then row_count({dbid}, p.id, p.partitionid) else null end as row_count_estimate, reserved_pages({dbid}, p.id, p.indid, p.partitionid) as reserved_pages, used_pages({dbid}, p.id, p.indid, p.partitionid) as used_pages from {db}syspartitions p where p.id = {number} order by p.indid, p.partitionid";
    }
    public static string AseIndexKeys(string database, string owner, string table, int id, string? index = null)
    {
        string db = Db(database), number = Id(id);
        string target = ResultFormats.Literal(ResultFormats.Identifier(database) + "." + ResultFormats.Identifier(owner) + "." + ResultFormats.Identifier(table));
        // Only an allpages clustered index omits the extra locator from keycnt (ASE sysindexes contract).
        string count = "(i.keycnt - case when i.indid = 1 then 0 else 1 end)";
        string filter = index == null ? "" : " and i.name = " + ResultFormats.Literal(index);
        return $"select i.name as index_name, i.indid, n.number as key_ordinal, {count} as key_count, index_col({target}, i.indid, n.number) as column_name, index_colorder({target}, i.indid, n.number) as sort_order, " +
            "case when (i.status & 16) != 0 or (i.status2 & 512) != 0 then 1 else 0 end as is_clustered, case when (i.status & 2) != 0 then 1 else 0 end as is_unique, " +
            "case when (i.status & 2048) != 0 then 1 else 0 end as primary_key, case when (i.status2 & 2) != 0 and (i.status & 2048) = 0 then 1 else 0 end as unique_constraint, case when (i.status2 & 32768) != 0 then 1 else 0 end as function_based " +
            $"from {db}sysindexes i, master..spt_values n where i.id = {number} and i.indid between 1 and 254 and n.type = 'P' and n.number between 1 and {count}{filter} order by i.indid, n.number";
    }
    public static string AseForeignKeys(string database, int id)
    {
        string db = Db(database), number = Id(id), dbid = DbId(database);
        string foreign = "case n.number " + string.Join(" ", Enumerable.Range(1, 16).Select(n => $"when {n} then r.fokey{n}")) + " end";
        string referenced = "case n.number " + string.Join(" ", Enumerable.Range(1, 16).Select(n => $"when {n} then r.refkey{n}")) + " end";
        string refDb = "db_id(isnull(r.pmrydbname, " + ResultFormats.Literal(database) + "))";
        // Resolve the referenced owner in that database in a separate read; user_name() has no database argument.
        return $"select o.name as constraint_name, n.number as key_ordinal, r.keycnt as key_count, col_name(r.tableid, {foreign}, {dbid}) as column_name, " +
            $"isnull(r.pmrydbname, {ResultFormats.Literal(database)}) as ref_database, object_owner_id(r.reftabid, {refDb}) as ref_owner_id, object_name(r.reftabid, {refDb}) as ref_table, col_name(r.reftabid, {referenced}, {refDb}) as ref_column, " +
            $"case when (r.status & 2) != 0 then 1 else 0 end as match_full from {db}sysreferences r join {db}sysobjects o on o.id = r.constrid, master..spt_values n where r.tableid = {number} and n.type = 'P' and n.number between 1 and r.keycnt order by o.name, n.number";
    }
    public static string AseUsers(string database, IEnumerable<int> ids) => $"select uid, name from {Db(database)}sysusers where uid in (" +
        string.Join(",", ids.Distinct().Select(id => id.ToString(CultureInfo.InvariantCulture))) + ") order by uid";

    public static string AseTableDdl(string database, string owner, string name, int id)
    {
        string db = Db(database), number = Id(id);
        string defaultId = "isnull(nullif(c.cdefault, 0), t.tdefault)", ruleId = "isnull(nullif(c.domain, 0), t.domain)";
        return $"select o.name, u.name as owner, lockscheme(o.id, {DbId(database)}) as lock_scheme from {db}sysobjects o join {db}sysusers u on u.uid = o.uid where o.id = {number} and o.type = 'U';\n" +
            $"select c.colid as ordinal, c.name, t.name as type, tu.name as type_owner, case when t.usertype > 100 then 1 else 0 end as is_user_type, c.length, @@ncharsize as national_char_size, c.prec as [precision], c.scale, " +
            "case when (c.status & 8) != 0 then 1 else 0 end as nullable, case when (c.status & 128) != 0 then 1 else 0 end as [identity], isnull(c.status2, 0) as status2, " +
            $"{defaultId} as default_id, du.name as default_owner, d.name as default_name, {ruleId} as rule_id, ru.name as rule_owner, r.name as rule_name " +
            $"from {db}syscolumns c join {db}systypes t on t.usertype = c.usertype left join {db}sysusers tu on tu.uid = t.uid " +
            $"left join {db}sysobjects d on d.id = {defaultId} left join {db}sysusers du on du.uid = d.uid left join {db}sysobjects r on r.id = {ruleId} left join {db}sysusers ru on ru.uid = r.uid " +
            $"where c.id = {number} order by c.colid;\n" + AseIndexKeys(database, owner, name, id) + ";\n" + AseForeignKeys(database, id) + ";\n" +
            // EXISTS avoids duplicating fragments when columns share a bound default/rule.
            $"select x.id, case when (isnull(x.status, 0) & 1) = 0 then x.text else convert(varchar(255), null) end as text from {db}syscomments x where x.texttype = 0 and (exists (select 1 from {db}syscolumns c join {db}systypes t on t.usertype = c.usertype where c.id = {number} and (x.id = {defaultId} or x.id = {ruleId})) or exists (select 1 from {db}sysconstraints k join {db}sysobjects o on o.id = k.constrid where k.tableid = {number} and (k.status & 128) != 0 and o.type = 'R' and x.id = k.constrid)) order by x.id, x.number, x.colid2, x.colid;\n" +
            $"select distinct o.id, o.name from {db}sysconstraints k join {db}sysobjects o on o.id = k.constrid where k.tableid = {number} and (k.status & 128) != 0 and o.type = 'R' order by o.id";
    }
    public static string AseUserType(string database, int id)
    {
        string db = Db(database);
        // Nullable physical variants share type IDs; the canonical built-in is the lowest matching usertype.
        return $"select u.name as owner, t.name, b.name as type, 0 as is_user_type, t.length, @@ncharsize as national_char_size, t.prec as [precision], t.scale, t.allownulls as nullable, isnull(t.ident, 0) as [identity], t.tdefault as default_id, t.domain as rule_id, du.name as default_owner, d.name as default_name, ru.name as rule_owner, r.name as rule_name " +
            $"from {db}systypes t join {db}sysusers u on u.uid = t.uid join {db}systypes b on b.type = t.type and b.usertype = (select min(s.usertype) from {db}systypes s where s.type = t.type and s.usertype < 100) left join {db}sysobjects d on d.id = t.tdefault left join {db}sysusers du on du.uid = d.uid left join {db}sysobjects r on r.id = t.domain left join {db}sysusers ru on ru.uid = r.uid where t.usertype = {Id(id)} and t.usertype > 100";
    }
}
