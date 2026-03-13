using System.Windows;
using System.Windows.Controls;
using Spark.Services;
///////////////////////////////////////////////
namespace Spark.Wizard;

/// <summary>
/// New Project wizard — multi-step dialog for creating a Spark project
/// with optional Ollama-powered story and prompt generation.
/// </summary>
partial class WizardWindow : Window
{
    readonly WizardViewModel m_vm;
    readonly StackPanel[] m_steps;

    /// <summary>Path to the created spark_project.json, or null if cancelled.</summary>
    public string? CreatedProjectPath { get; private set; }

    public WizardWindow()
    {
        InitializeComponent();
        m_vm = new WizardViewModel(ServiceHost.Instance.Require<OllamaClient>());
        DataContext = m_vm;

        m_steps = [Step0, Step1, Step2, Step3, Step4];

        m_vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WizardViewModel.CurrentStep))
                ShowStep(m_vm.CurrentStep);
        };

        m_vm.ProjectCreated += path =>
        {
            CreatedProjectPath = path;
            DialogResult = true;
            Close();
        };

        Owner = Application.Current.MainWindow;
    }

    void ShowStep(int step)
    {
        for (int i = 0; i < m_steps.Length; i++)
            m_steps[i].Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;
    }
}
