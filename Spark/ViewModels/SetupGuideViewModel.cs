using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using Common.Wpf.Input;
using Common.Wpf.ViewModels;
///////////////////////////////////////////
namespace Spark.ViewModels;

sealed class SetupGuideViewModel : ObservableObject
{
    string m_headerText = "";
    string m_scanStatus = "";
    string m_statusText = "";
    Brush m_statusForeground = Brushes.Gray;
    Brush m_statusBackground = Brushes.Transparent;
    bool m_isScanning;
    bool m_showRepairButton;
    bool m_showResetVenvButton;

    public ObservableCollection<GuideItem> Items { get; } = [];

    public string HeaderText { get => m_headerText; set => SetField(ref m_headerText, value); }
    public string ScanStatus { get => m_scanStatus; set => SetField(ref m_scanStatus, value); }
    public string StatusText { get => m_statusText; set => SetField(ref m_statusText, value); }
    public Brush StatusForeground { get => m_statusForeground; set => SetField(ref m_statusForeground, value); }
    public Brush StatusBackground { get => m_statusBackground; set => SetField(ref m_statusBackground, value); }

    public bool IsScanning
    {
        get => m_isScanning;
        set
        {
            SetField(ref m_isScanning, value);
            OnPropertyChanged(nameof(CanRescan));
        }
    }

    public bool ShowRepairButton { get => m_showRepairButton; set => SetField(ref m_showRepairButton, value); }
    public bool ShowResetVenvButton { get => m_showResetVenvButton; set => SetField(ref m_showResetVenvButton, value); }
    public bool CanRescan => !m_isScanning;

    public ICommand RescanCommand { get; set; } = RelayCommand.Empty;
    public ICommand OpenPageCommand { get; set; } = RelayCommand.Empty;
    public ICommand OpenTerminalCommand { get; set; } = RelayCommand.Empty;
    public ICommand AutoRepairCommand { get; set; } = RelayCommand.Empty;
    public ICommand ResetVenvCommand { get; set; } = RelayCommand.Empty;

    public event EventHandler? RequestClose;
    public void Close() => RequestClose?.Invoke(this, EventArgs.Empty);
}
