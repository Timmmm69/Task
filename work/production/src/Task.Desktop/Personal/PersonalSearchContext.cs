using Task.Desktop.Work;

namespace Task.Desktop.Personal;

public sealed record PersonalSearchRow(DesktopSearchResult Source, string Context, string Metadata, string TypeLabel, string Query)
{
    public string Title => Source.Title;
    public string DetailText => string.IsNullOrWhiteSpace(Context) ? "Без описания" : Context;
    public string Preview
    {
        get
        {
            var text = DetailText.ReplaceLineEndings(" · ");
            var index = text.IndexOf(Query.Trim(), StringComparison.CurrentCultureIgnoreCase);
            var start = Math.Max(0, index - 45);
            var length = Math.Min(190, text.Length - start);
            return (start > 0 ? "…" : "") + text.Substring(start, length) + (start + length < text.Length ? "…" : "");
        }
    }
}

public sealed partial class PersonalTaskStore
{
    // Presentation only: use the same searchable fields and lifecycle as SearchPersonal.
    internal IReadOnlyDictionary<Guid, (string Context, string Detail)> SearchContexts() => Locked(() =>
    {
        using var c = Command("""
            SELECT o.id,COALESCE(o.search_text,''),o.object_type,t.status,p.status,c.item_type
            FROM personal_objects o
            LEFT JOIN tasks t ON t.id=o.id AND o.object_type='task'
            LEFT JOIN personal_projects p ON p.id=o.id AND o.object_type='project'
            LEFT JOIN personal_catalog c ON c.id=o.id AND o.object_type='catalog_item'
            WHERE o.lifecycle='active';
            """);
        using var r = c.ExecuteReader();
        var rows = new Dictionary<Guid, (string, string)>();
        while (r.Read())
        {
            var detail = r.GetString(2) switch
            {
                "task" => r.GetInt32(3) switch { 0 => "Новая", 1 => "В работе", 2 => "На проверке", 3 => "Завершена", _ => "Отменена" },
                "project" => r.GetInt32(4) switch { 0 => "Планирование", 1 => "Активен", 2 => "Приостановлен", _ => "Завершён" },
                "catalog_item" => r.GetString(5) switch { "virtual_folder" => "Виртуальная папка", "folder_reference" => "Папка на диске", _ => "Ссылка на файл" },
                _ => "Личный контакт"
            };
            rows.Add(Guid.Parse(r.GetString(0)), (r.GetString(1), detail));
        }
        return (IReadOnlyDictionary<Guid, (string Context, string Detail)>)rows;
    });
}
