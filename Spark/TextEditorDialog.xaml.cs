using System.Windows;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// A simple in-app text editor dialog for prompts, story files, and universe config.
/// Replaces shelling out to an external editor so users stay in Spark.
/// </summary>
partial class TextEditorDialog : Window
{
    readonly string m_fileName;
    readonly DocumentStore? m_docStore;
    readonly Action<string>? m_onSave;

    public bool WasSaved { get; private set; }
    public string EditedText => Editor.Text;

    /// <param name="title">Dialog title</param>
    /// <param name="fileName">Document filename (for DocumentStore operations)</param>
    /// <param name="content">Initial content to edit</param>
    /// <param name="hint">Help text shown below the title</param>
    /// <param name="docStore">If provided, saves through the document store</param>
    /// <param name="onSave">Callback after save</param>
    public TextEditorDialog(
        string title, string fileName, string content,
        string hint = "",
        DocumentStore? docStore = null,
        Action<string>? onSave = null)
    {
        InitializeComponent();
        m_fileName = fileName;
        m_docStore = docStore;
        m_onSave = onSave;

        Title = $"Edit — {fileName}";
        HeaderText.Text = title;
        HintText.Text = hint;
        Editor.Text = content;

        int lines = content.Split('\n').Length;
        int words = content.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
        StatusText.Text = $"{lines} lines  •  {words} words";

        Editor.TextChanged += (_, _) =>
        {
            int l = Editor.Text.Split('\n').Length;
            int w = Editor.Text.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
            StatusText.Text = $"{l} lines  •  {w} words  •  modified";
        };

        Owner = Application.Current.MainWindow;
    }

    void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (m_docStore is not null)
            m_docStore.Put(m_fileName, Editor.Text);

        m_onSave?.Invoke(Editor.Text);
        WasSaved = true;
        DialogResult = true;
        Close();
    }

    void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
