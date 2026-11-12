// How a person is named next to the things they did on an item (who uploaded a file, who
// opened a revision, who entered the price): first and last name, because that is what goes
// into the drawing title block and what people recognise. Accounts created before migration
// 066 -- and the default "admin" -- may not have them yet, so the display name stands in until
// someone fills them in under Settings -> Users.
static class UserNames
{
    // SQL expression for a users row aliased as `alias`; NULL only when there is no user
    // (deleted account, ON DELETE SET NULL).
    public static string FullNameSql(string alias) =>
        $"COALESCE(NULLIF(btrim(concat_ws(' ', {alias}.first_name, {alias}.last_name)), ''), {alias}.display_name)";
}
