using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Input;
using System.Windows.Forms;
/////////////////////////////////
namespace SpatialGame.ViewModels;

public class DirectionalLight : ViewModel
{
    Vector3D direction;
    Color color;
    bool on;
    public ICommand PickColorCommand { get; private set; }

    public DirectionalLight(Vector3D direction, Color color, bool on = true)
    {
        this.direction = direction;
        this.color = color;
        this.on = on;
        PickColorCommand = new RelayCommand(_ => PickColor());
    }

    void PickColor()
    {
        var dlg = new ColorDialog { Color = System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B) };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            color = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
            OnPropertyChanged(nameof(Color));
        }
    }
    public Vector3D Direction
    {
        get => direction;
        set { direction = value; OnPropertyChanged(); }
    }
    public double DirectionX
    {
        get => direction.X;
        set { direction.X = value; OnPropertyChanged(); OnPropertyChanged(nameof(Direction)); }
    }
    public double DirectionY
    {
        get => direction.Y;
        set { direction.Y = value; OnPropertyChanged(); OnPropertyChanged(nameof(Direction)); }
    }
    public double DirectionZ
    {
        get => direction.Z;
        set { direction.Z = value; OnPropertyChanged(); OnPropertyChanged(nameof(Direction)); }
    }
    public Color Color
    {
        get => color;
        set { color = value; OnPropertyChanged(); }
    }
    public bool On
    {
        get => on;
        set { on = value; OnPropertyChanged(); }
    }
}

public class PointLight : ViewModel
{
    Point3D position;
    Color color;
    double range;
    bool on;
    double rangeMin;
    double rangeMax;
    public ICommand PickColorCommand { get; }
    public PointLight(Point3D position, Color color, double range, bool on = false, double rangeMin = 10, double rangeMax = 1000)
    {
        this.position = position;
        this.color = color;
        this.range = range;
        this.on = on;
        this.rangeMin = rangeMin;
        this.rangeMax = rangeMax;
        PickColorCommand = new RelayCommand(_ => PickColor());
    }
    void PickColor()
    {
        var dlg = new ColorDialog { Color = System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B) };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            color = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
            OnPropertyChanged(nameof(Color));
        }
    }
    public Point3D Position
    {
        get => position;
        set { position = value; OnPropertyChanged(); }
    }
    public double PositionX
    {
        get => position.X;
        set { position.X = value; OnPropertyChanged(); OnPropertyChanged(nameof(Position)); }
    }
    public double PositionY
    {
        get => position.Y;
        set { position.Y = value; OnPropertyChanged(); OnPropertyChanged(nameof(Position)); }
    }
    public double PositionZ
    {
        get => position.Z;
        set { position.Z = value; OnPropertyChanged(); OnPropertyChanged(nameof(Position)); }
    }
    public Color Color
    {
        get => color;
        set { color = value; OnPropertyChanged(); }
    }
    public double Range
    {
        get => range;
        set { range = value; OnPropertyChanged(); }
    }
    public bool On
    {
        get => on;
        set { on = value; OnPropertyChanged(); }
    }
    public double RangeMin
    {
        get => rangeMin;
        set { rangeMin = value; OnPropertyChanged(); }
    }
    public double RangeMax
    {
        get => rangeMax;
        set { rangeMax = value; OnPropertyChanged(); }
    }
}

public class SpotLight : ViewModel
{
    Point3D position;
    Vector3D direction;
    Color color;
    double range;
    bool on;
    double rangeMin;
    double rangeMax;
    double outerAngle;
    double outerAngleMin;
    double outerAngleMax;
    double innerAngle;
    double innerAngleMin;
    double innerAngleMax;
    double falloff;
    double falloffMin;
    double falloffMax;
    public ICommand PickColorCommand { get; }
    public SpotLight(
        Point3D position,
        Vector3D direction,
        Color color,
        double range,
        bool on = true,
        double rangeMin = 10,
        double rangeMax = 2000,
        double outerAngle = 60,
        double outerAngleMin = 10,
        double outerAngleMax = 120,
        double innerAngle = 30,
        double innerAngleMin = 0,
        double innerAngleMax = 120,
        double falloff = 1.0,
        double falloffMin = 0.1,
        double falloffMax = 5.0)
    {
        this.position = position;
        this.direction = direction;
        this.color = color;
        this.range = range;
        this.on = on;
        this.rangeMin = rangeMin;
        this.rangeMax = rangeMax;
        this.outerAngle = outerAngle;
        this.outerAngleMin = outerAngleMin;
        this.outerAngleMax = outerAngleMax;
        this.innerAngle = innerAngle;
        this.innerAngleMin = innerAngleMin;
        this.innerAngleMax = innerAngleMax;
        this.falloff = falloff;
        this.falloffMin = falloffMin;
        this.falloffMax = falloffMax;
        PickColorCommand = new RelayCommand(_ => PickColor());
    }

    void PickColor()
    {
        var dlg = new ColorDialog { Color = System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B) };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            color = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
            OnPropertyChanged(nameof(Color));
        }
    }
    public Point3D Position
    {
        get => position;
        set { position = value; OnPropertyChanged(); }
    }
    public double PositionX
    {
        get => position.X;
        set { position.X = value; OnPropertyChanged(); OnPropertyChanged(nameof(Position)); }
    }
    public double PositionY
    {
        get => position.Y;
        set { position.Y = value; OnPropertyChanged(); OnPropertyChanged(nameof(Position)); }
    }
    public double PositionZ
    {
        get => position.Z;
        set { position.Z = value; OnPropertyChanged(); OnPropertyChanged(nameof(Position)); }
    }
    public Vector3D Direction
    {
        get => direction;
        set { direction = value; OnPropertyChanged(); }
    }
    public double DirectionX
    {
        get => direction.X;
        set { direction.X = value; OnPropertyChanged(); OnPropertyChanged(nameof(Direction)); }
    }
    public double DirectionY
    {
        get => direction.Y;
        set { direction.Y = value; OnPropertyChanged(); OnPropertyChanged(nameof(Direction)); }
    }
    public double DirectionZ
    {
        get => direction.Z;
        set { direction.Z = value; OnPropertyChanged(); OnPropertyChanged(nameof(Direction)); }
    }
    public Color Color
    {
        get => color;
        set { color = value;  OnPropertyChanged(); }
    }
    public double Range
    {
        get => range;
        set { range = value; OnPropertyChanged(); }
    }
    public bool On
    {
        get => on;
        set { on = value; OnPropertyChanged(); }
    }
    public double RangeMin
    {
        get => rangeMin;
        set { rangeMin = value; OnPropertyChanged(); }
    }
    public double RangeMax
    {
        get => rangeMax;
        set { rangeMax = value; OnPropertyChanged(); }
    }
    public double OuterAngle
    {
        get => outerAngle;
        set { outerAngle = value; OnPropertyChanged(); }
    }
    public double OuterAngleMin
    {
        get => outerAngleMin;
        set { outerAngleMin = value; OnPropertyChanged(); }
    }
    public double OuterAngleMax
    {
        get => outerAngleMax;
        set { outerAngleMax = value; OnPropertyChanged(); }
    }
    public double InnerAngle
    {
        get => innerAngle;
        set { innerAngle = value; OnPropertyChanged(); }
    }
    public double InnerAngleMin
    {
        get => innerAngleMin;
        set { innerAngleMin = value; OnPropertyChanged(); }
    }
    public double InnerAngleMax
    {
        get => innerAngleMax;
        set { innerAngleMax = value; OnPropertyChanged(); }
    }
    public double Falloff
    {
        get => falloff;
        set { falloff = value; OnPropertyChanged(); }
    }
    public double FalloffMin
    {
        get => falloffMin;
        set { falloffMin = value; OnPropertyChanged(); }
    }
    public double FalloffMax
    {
        get => falloffMax;
        set { falloffMax = value; OnPropertyChanged(); }
    }
}

public class AmbientLight(Color color, bool on = true) : ViewModel
{
    private Color color = color;
    private bool on = on;

    public Color Color
    {
        get => color;
        set { color = value; OnPropertyChanged(); }
    }

    public bool On
    {
        get => on;
        set { on = value; OnPropertyChanged(); }
    }
}

public abstract class ViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class RelayCommand : ICommand
{
    readonly Action<object?> execute;
    readonly Func<object?, bool>? canExecute;
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
