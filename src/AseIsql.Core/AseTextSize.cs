using System.Text.RegularExpressions;

namespace AseIsql;

// One instance per connection. Initialize the plan XML allowance once, then leave the session's textsize alone.
public sealed class AseTextSize
{
    private static readonly Regex SetTextSize = new(@"(?:^|[^\w@#$])set\s+textsize(?:$|[^\w@#$])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private bool initialized, userControlled;

    // Observe before submission: even a failed/cancelled batch may have applied SET before it stopped. Detection
    // deliberately gives the user control for expressions and conditional SETs too, without guessing their value.
    public void ObserveBatch(string sql)
    {
        if (!userControlled && SetTextSize.IsMatch(SqlRisk.CodeOnly(sql))) userControlled = true;
    }

    public void EnsureForExecutionIo(Action<string> execute)
    {
        if (initialized || userControlled) return;
        execute("set textsize 16777216");
        initialized = true;
    }
}
