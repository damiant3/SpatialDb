using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
///////////////////////////////////////////////
namespace Spark;

partial class DocumentManagerDialog : Window
{
    readonly DocumentStore m_docs;
    readonly SparkProject m_project;
    readonly Action<string> m_log;

    public DocumentManagerDialog(DocumentStore docs, SparkProject project, Action<string> log)
    {
        InitializeComponent();
        m_docs = docs;
        m_project = project;
        m_log = log;
        Owner = Application.Current.MainWindow;
        RefreshList();
    }

    void RefreshList()
    {
        DocList.ItemsSource = m_docs.Entries
            .Select(e => new DocListItem
            {
                FileName = e.FileName,
                Role = e.Role,
                KeywordCount = e.Keywords.Length,
                RoleIcon = e.Role switch
                {
                    "prompts" => "📝",
                    "universe" => "🌌",
                    "characters" => "👤",
                    "locations" => "🏛",
                    _ => "📄",
                },
            })
            .OrderBy(d => d.FileName)
            .ToList();

        StatusLabel.Text = $"{m_docs.Count} documents indexed";
    }

    void OnDocDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DocList.SelectedItem is DocListItem item)
            EditDocument(item.FileName);
    }

    void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DocListItem item })
            EditDocument(item.FileName);
    }

    void EditDocument(string fileName)
    {
        string? content = m_docs.Get(fileName);
        if (content is null)
        {
            string path = Path.Combine(m_project.ProjectDir, fileName);
            content = File.Exists(path) ? File.ReadAllText(path) : "";
        }

        string role = m_docs.Entries.FirstOrDefault(
            e => e.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))?.Role ?? "reference";

        TextEditorDialog editor = new(
            title: $"Editing: {fileName}",
            fileName: fileName,
            content: content,
            hint: $"Role: {role}. This file is indexed for prompt conditioning — relevant sections are automatically injected into generation context.",
            docStore: m_docs,
            onSave: _ =>
            {
                m_log($"📄 Saved: {fileName}");
                RefreshList();
            });

        editor.ShowDialog();
    }

    void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DocListItem item }) return;
        MessageBoxResult result = MessageBox.Show(
            $"Delete {item.FileName}?\n\nThis will remove the file from disk.",
            "Delete Document", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        string path = Path.Combine(m_project.ProjectDir, item.FileName);
        try
        {
            if (File.Exists(path)) File.Delete(path);
            m_docs.Ingest(m_project.StoryFiles.Length > 0 ? m_project.StoryFiles : ["*.txt", "*.md"]);
            m_log($"🗑 Deleted document: {item.FileName}");
            RefreshList();
        }
        catch (Exception ex) { m_log($"Error deleting: {ex.Message}"); }
    }

    void OnNewDocClick(object sender, RoutedEventArgs e)
    {
        // Simple new document flow
        string name = $"notes_{DateTime.Now:yyyyMMdd_HHmmss}.md";
        TextEditorDialog editor = new(
            title: "New Document",
            fileName: name,
            content: $"# {name}\n\nAdd your notes, lore, character descriptions, or reference material here.\n",
            hint: "This file will be indexed and used for relevance-based prompt conditioning. " +
                  "Write freely — the most relevant sections will be automatically pulled into generation context.",
            docStore: m_docs,
            onSave: _ =>
            {
                m_log($"📄 Created: {name}");
                RefreshList();
            });

        editor.ShowDialog();
    }

    void OnRescanClick(object sender, RoutedEventArgs e)
    {
        string[] patterns = m_project.StoryFiles.Length > 0
            ? m_project.StoryFiles : ["*.txt", "*.md"];
        int added = m_docs.Ingest(patterns);
        m_log($"Re-scanned project: {added} new documents");
        RefreshList();
    }

    void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

sealed class DocListItem
{
    public string FileName { get; init; } = "";
    public string Role { get; init; } = "";
    public int KeywordCount { get; init; }
    public string RoleIcon { get; init; } = "📄";
}
