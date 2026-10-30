-- Type fidelity sweep for SAP ASE.
--
-- Purpose: find values a client reads, converts or displays differently from the server, including the ones that
-- fail silently. Run the whole file in DBMON SQL and the same file in ASE ISQL, then compare the two outputs cell
-- by cell. Anything that differs, is blank, is rounded or is shorter on one side is a finding.
--
-- The sweep reads nothing of yours and writes nothing: every batch is a SELECT over constants, except the last
-- one, which reads the system catalog. Each case is its own batch, so a type your server version does not support
-- (unsigned bigint, bigdatetime and bigtime need ASE 15.7 or later) fails alone and the rest still run.
--
-- What to look at, beyond values being equal:
--   * every digit present on both sides, with no rounding and no exponent swapped in;
--   * fractions of a second kept (ASE datetime holds 1/300 s, bigdatetime microseconds);
--   * NULL shown as NULL, never as an empty string or a zero;
--   * trailing spaces preserved (the markers below make them visible);
--   * binary identical, including leading zero bytes.

select 'numeric 38,0 max' as sweep_case,
       convert(numeric(38,0), '99999999999999999999999999999999999999') as value_max,
       convert(numeric(38,0), '-99999999999999999999999999999999999999') as value_min
go

select 'numeric scale' as sweep_case,
       convert(numeric(38,10), '1234567890123456789012345678.0123456789') as wide_scale,
       convert(numeric(18,6), '123456789012.345678') as common_scale,
       convert(numeric(38,0), null) as null_numeric
go

select 'integers' as sweep_case,
       convert(bigint, 9223372036854775807) as bigint_max,
       convert(bigint, -9223372036854775808) as bigint_min,
       convert(int, 2147483647) as int_max,
       convert(smallint, -32768) as smallint_min,
       convert(tinyint, 255) as tinyint_max
go

-- ASE 15.7 and later. A client that reads these as signed integers reports a negative number without any error.
select 'unsigned integers' as sweep_case,
       convert(unsigned bigint, 18446744073709551615) as ubigint_max,
       convert(unsigned int, 4294967295) as uint_max,
       convert(unsigned smallint, 65535) as usmallint_max
go

select 'approximate numbers' as sweep_case,
       convert(float, 0.1) as float_tenth,
       convert(float, 1.2345678901234567) as float_17_digits,
       convert(float, 1.7976931348623157e308) as float_max,
       convert(real, 1.17549435e-38) as real_small
go

select 'money' as sweep_case,
       convert(money, 922337203685477.5807) as money_max,
       convert(money, -922337203685477.5808) as money_min,
       convert(smallmoney, 214748.3647) as smallmoney_max
go

-- Three moments inside the same second. A grid that hides the fraction shows these as one repeated value.
select 'datetime fraction' as sweep_case,
       convert(datetime, '2026-02-03 16:30:45.123') as moment_a,
       convert(datetime, '2026-02-03 16:30:45.126') as moment_b,
       convert(datetime, '2026-02-03 16:30:45.130') as moment_c,
       convert(datetime, '2026-02-03 16:30:30.000') as second_ending_in_zero
go

select 'datetime range' as sweep_case,
       convert(datetime, '1753-01-01 00:00:00.000') as datetime_min,
       convert(datetime, '9999-12-31 23:59:59.996') as datetime_max,
       convert(smalldatetime, '2026-02-03 16:30:00') as smalldatetime_value,
       convert(datetime, null) as null_datetime
go

-- ASE 15.7 and later: microsecond precision, and dates below the datetime floor.
select 'bigdatetime' as sweep_case,
       convert(bigdatetime, '2026-02-03 16:30:45.123456') as microseconds,
       convert(bigdatetime, '0001-01-01 00:00:00.000000') as bigdatetime_min,
       convert(bigtime, '16:30:45.123456') as bigtime_value,
       datepart(us, convert(bigtime, '16:30:45.123456')) as bigtime_microseconds
go

-- The fraction is asked for as a number, not as text. A plain convert to varchar would answer in the server's
-- default style, which drops the seconds along with the fraction, and a style that keeps them is one more thing
-- to get right; datepart returns an int, which reaches the client without any date conversion at all. So the
-- number beside each value says what the server holds: 123 against a time_value of 16:30:45 means the fraction
-- exists and was lost on the way, while 0 means the server never had it.
select 'date and time' as sweep_case,
       convert(date, '2026-02-03') as date_value,
       convert(time, '16:30:45.123') as time_value,
       datepart(ms, convert(time, '16:30:45.123')) as time_milliseconds,
       convert(time, '00:00:00.000') as midnight
go

-- The markers make trailing spaces visible on both sides; char(10) pads, varchar does not.
select 'character padding' as sweep_case,
       '[' + convert(char(10), 'ab') + ']' as padded_char,
       '[' + convert(varchar(10), 'ab  ') + ']' as varchar_with_spaces,
       '[' + convert(varchar(10), '') + ']' as empty_varchar,
       datalength(convert(char(10), 'ab')) as char_bytes,
       datalength(convert(varchar(10), '')) as empty_bytes
go

select 'unicode and accents' as sweep_case,
       convert(varchar(40), 'áéíóú ñÑ üÜ ¿¡') as latin_text,
       convert(univarchar(40), 'áéíóú 漢字 кириллица') as unicode_text,
       char_length(convert(univarchar(40), '漢字')) as unicode_characters,
       datalength(convert(univarchar(40), '漢字')) as unicode_bytes
go

-- The same characters as bytes. Character data is converted to the session's charset on its way back, and a
-- console has a codepage of its own on top of that, so a row of question marks says nothing about where the
-- characters were lost. Binary is converted by nobody: the hex below is what the server holds. 漢 is U+6F22 and
-- 字 is U+5B57, so cjk_as_bytes carries 6F22 5B57 or its byte-swapped form, in whichever order the server keeps
-- UTF-16. Any other value, or a short one, means the characters never reached the server.
select 'unicode as bytes' as sweep_case,
       convert(varbinary(8), convert(univarchar(4), '漢字')) as cjk_as_bytes,
       convert(varbinary(16), convert(univarchar(8), 'кирилл')) as cyrillic_as_bytes,
       convert(varbinary(90), convert(univarchar(40), 'áéíóú 漢字 кириллица')) as whole_string_as_bytes
go

-- A character literal in a query is ordinary character data: ASE converts it to the server's default charset
-- before anything becomes univarchar, so a character that charset cannot represent is already a substitution
-- character by the time it is stored. These values are built from their code points as binary instead, which
-- carries no charset at all, so they reach a univarchar column intact and this batch measures the way back on
-- its own. U+6F22 and U+5B57 are the two ideographs; the swapped pair is there to show the order of the bytes
-- the server keeps. Measured against ASEDB1 on 30/10/2026: cjk_text reads as 漢字 and cjk_bytes_back returns
-- 0x6F225B57 unchanged, so that server keeps UTF-16 in that order and the trip back loses nothing.
select 'unicode from bytes' as sweep_case,
       convert(univarchar(2), 0x6F225B57) as cjk_text,
       convert(univarchar(2), 0x226F575B) as cjk_text_swapped,
       convert(varbinary(8), convert(univarchar(2), 0x6F225B57)) as cjk_bytes_back
go

select 'binary' as sweep_case,
       convert(binary(8), 0x0001020304050607) as binary_with_leading_zero,
       convert(varbinary(8), 0xff00ff00) as varbinary_value,
       convert(binary(4), null) as null_binary
go

select 'long text' as sweep_case,
       datalength(replicate('x', 255)) as replicate_bytes,
       convert(text, replicate('abcdefghij', 25)) as text_value
go

select 'bit and nulls' as sweep_case,
       convert(bit, 1) as bit_one,
       convert(bit, 0) as bit_zero,
       convert(varchar(10), null) as null_varchar,
       convert(int, null) as null_int,
       convert(float, null) as null_float
go

-- The real catalog column that started this sweep: identburnmax is numeric(38,0) and overflows a 28-digit decimal.
-- Only tables with an identity column carry a counter, and master has none, so run these two in a database of
-- your own. The count comes first because it tells the difference between a client that returned nothing and a
-- database that has nothing to return: with_counter = 0 means this database cannot exercise the wide column.
select 'catalog wide numeric' as sweep_case,
       db_name() as database_name,
       count(*) as user_tables,
       count(identburnmax) as with_counter
  from sysobjects
 where type = 'U'
go

select top 20 'catalog wide numeric' as sweep_case,
       name, id, identburnmax, crdate
  from sysobjects
 where type = 'U'
   and identburnmax is not null
order by name
go
