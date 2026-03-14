using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
///////////////////////////////////////////////
namespace Spark.Controls;

partial class ExpandableLogPanel : UserControl
{
    bool m_expanded;

    public ExpandableLogPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (CompactLogList.ItemsSource is INotifyCollectionChanged ncc)
            ncc.CollectionChanged += OnLogCollectionChanged;
    }

    void OnLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        if (m_expanded)
        {
            if (ExpandedLogList.Items.Count > 0)
                ExpandedLogList.ScrollIntoView(ExpandedLogList.Items[^1]);
        }
        else
        {
            if (CompactLogList.Items.Count > 0)
                CompactLogList.ScrollIntoView(CompactLogList.Items[^1]);
        }
    }

    void OnHeaderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
            ToggleExpanded(true);
    }

    void OnCollapseClick(object sender, RoutedEventArgs e) => ToggleExpanded(false);

    void OnCopyAll(object sender, RoutedEventArgs e)
    {
        if (CompactLogList.ItemsSource is not System.Collections.IEnumerable items) return;
        System.Text.StringBuilder sb = new();
        foreach (object item in items)
            sb.AppendLine(item.ToString());
        if (sb.Length > 0)
            Clipboard.SetText(sb.ToString());
    }

    void ToggleExpanded(bool expand)
    {
        m_expanded = expand;
        ExpandedPopup.IsOpen = expand;

        if (expand && ExpandedLogList.Items.Count > 0)
            ExpandedLogList.ScrollIntoView(ExpandedLogList.Items[^1]);
    }
}
