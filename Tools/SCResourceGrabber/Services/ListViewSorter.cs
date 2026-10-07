using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SCResourceGrabber.Services;

/// <summary>
/// 목록 열 머리글 클릭 정렬. 지정한 열만 정렬된다.
/// 클릭할 때마다 오름차순(▲) → 내림차순(▼) → 정렬 해제(받은 순서) 순으로 바뀐다.
/// </summary>
public static class ListViewSorter
{
    private const string Arrows = " ▲▼";

    /// <param name="columns">머리글 글자 → 정렬할 속성 이름 (예: "크기" → "Size")</param>
    public static void Enable(ListView list, ICollectionView view, IReadOnlyDictionary<string, string> columns)
    {
        list.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is not GridViewColumnHeader { Column: { } column }) return;
            string title = BaseTitle(column);
            if (!columns.TryGetValue(title, out var property)) return;

            var current = view.SortDescriptions.Count > 0 ? view.SortDescriptions[0] : (SortDescription?)null;
            ListSortDirection? next = current?.PropertyName != property ? ListSortDirection.Ascending
                                    : current?.Direction == ListSortDirection.Ascending ? ListSortDirection.Descending
                                    : null;

            // 다른 열의 화살표 지우기
            if (list.View is GridView grid)
                foreach (var c in grid.Columns)
                    if (c.Header is string) c.Header = BaseTitle(c);

            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                if (next is { } dir) view.SortDescriptions.Add(new SortDescription(property, dir));
            }
            if (next is { } d) column.Header = title + (d == ListSortDirection.Ascending ? " ▲" : " ▼");
        }));
    }

    private static string BaseTitle(GridViewColumn c) => (c.Header as string ?? "").TrimEnd(Arrows.ToCharArray());
}
