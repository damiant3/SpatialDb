using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
///////////////////////////////////////////////
namespace Spark.Controls;

partial class GalleryPanel : UserControl
{
    Point? m_dragStart;
    const double SwipeThreshold = 40;

    public GalleryPanel() => InitializeComponent();

    // ── Drag-swipe to cycle stack ───────────────────────────────

    void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement card) return;
        // Select on click
        if (card.DataContext is PromptStack stack)
            stack.SelectCommand.Execute(null);
        m_dragStart = e.GetPosition(card);
        card.CaptureMouse();
    }

    void OnCardMouseMove(object sender, MouseEventArgs e)
    {
        // Handled on MouseUp for cleaner swipe detection
    }

    void OnCardMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement card) { m_dragStart = null; return; }
        card.ReleaseMouseCapture();

        if (m_dragStart is not null && card.DataContext is PromptStack stack && stack.HasMultiple)
        {
            Point end = e.GetPosition(card);
            double dx = end.X - m_dragStart.Value.X;
            if (dx > SwipeThreshold)
                stack.CycleForwardCommand.Execute(null);
            else if (dx < -SwipeThreshold)
                stack.CycleBackCommand.Execute(null);

            // Double-click opens lightbox
            if (Math.Abs(dx) < 5 && e.ClickCount == 2)
            {
                if (DataContext is MainViewModel vm)
                    vm.ShowLightboxCommand.Execute(null);
            }
        }
        else if (m_dragStart is not null && card.DataContext is PromptStack s2)
        {
            Point end = e.GetPosition(card);
            double dx = end.X - m_dragStart.Value.X;
            if (Math.Abs(dx) < 5 && e.ClickCount == 2)
            {
                if (DataContext is MainViewModel vm)
                    vm.ShowLightboxCommand.Execute(null);
            }
        }

        m_dragStart = null;
    }

    // ── Context menu ────────────────────────────────────────────

    void OnCardContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement card) return;
        if (DataContext is not MainViewModel vm) return;

        if (card.DataContext is PromptStack stack)
            stack.SelectCommand.Execute(null);

        ContextMenu menu = new();

        foreach (ArtDirections.DirectionGroup group in ArtDirections.Groups)
        {
            MenuItem groupItem = new() { Header = $"{group.Emoji} {group.Category}" };
            foreach (ArtDirections.Direction dir in group.Items)
            {
                MenuItem dirItem = new()
                {
                    Header = dir.Label,
                    Command = vm.DirectedRegenCommand,
                    CommandParameter = dir.Label,
                };
                groupItem.Items.Add(dirItem);
            }
            menu.Items.Add(groupItem);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = "🧬 Mutate",
            Command = vm.CreativeRegenCommand,
            FontWeight = FontWeights.Bold,
        });

        card.ContextMenu = menu;
    }
}
