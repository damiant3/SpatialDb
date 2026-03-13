using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
///////////////////////////////////////////////
namespace Spark;

partial class LightboxWindow : Window
{
    readonly PromptStack? m_stack;
    readonly Action<ImageRecord>? m_onSelect;
    Point m_dragStart;
    bool m_dragging;

    public LightboxWindow(ImageRecord image, PromptStack? stack = null, Action<ImageRecord>? onSelect = null)
    {
        InitializeComponent();
        m_stack = stack;
        m_onSelect = onSelect;
        ShowImage(image);
    }

    void ShowImage(ImageRecord image)
    {
        try
        {
            BitmapImage bmp = new();
            bmp.BeginInit();
            bmp.UriSource = new Uri(image.FilePath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            FullImage.Source = bmp;
        }
        catch { FullImage.Source = null; }

        string seed = image.Seed > 0 ? image.Seed.ToString() : "random";
        string size = image.SourceWidth > 0 ? $"{image.SourceWidth}×{image.SourceHeight}" : "";
        InfoText.Text = $"{image.DisplayName}  •  Seed: {seed}  •  {size}  •  {image.RatingStars}";

        ScaleXform.ScaleX = 1;
        ScaleXform.ScaleY = 1;
        TranslateXform.X = 0;
        TranslateXform.Y = 0;
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Left:
                CycleStack(-1);
                break;
            case Key.Right:
                CycleStack(1);
                break;
        }
    }

    void CycleStack(int direction)
    {
        if (m_stack is null || m_stack.Cards.Count < 2) return;
        if (direction > 0)
            m_stack.CycleForwardCommand.Execute(null);
        else
            m_stack.CycleBackCommand.Execute(null);

        if (m_stack.TopCard is not null)
        {
            ShowImage(m_stack.TopCard);
            m_onSelect?.Invoke(m_stack.TopCard);
        }
    }

    void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        double newScale = Math.Clamp(ScaleXform.ScaleX * factor, 0.1, 20.0);
        ScaleXform.ScaleX = newScale;
        ScaleXform.ScaleY = newScale;
    }

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { Close(); return; }
        m_dragging = true;
        m_dragStart = e.GetPosition(this);
        CaptureMouse();
    }

    void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        m_dragging = false;
        ReleaseMouseCapture();
    }

    void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!m_dragging) return;
        Point pos = e.GetPosition(this);
        TranslateXform.X += pos.X - m_dragStart.X;
        TranslateXform.Y += pos.Y - m_dragStart.Y;
        m_dragStart = pos;
    }
}
