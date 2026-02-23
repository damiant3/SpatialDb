using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Input;
using System.Windows.Forms;
/////////////////////////////////
namespace SpatialGame.ViewModels;

public class DirectionalLight(Vector3D direction, Color color, bool on = false) : ColorViewModel(color, on)
{
    Vector3D direction = direction;

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
}

public class PointLight(Point3D position, Color color, double range, bool on = true, double rangeMin = 10, double rangeMax = 1000) : ColorViewModel(color, on)
{
    Point3D position = position;
    double range = range;
    double rangeMin = rangeMin;
    double rangeMax = rangeMax;

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
    public double Range
    {
        get => range;
        set { range = value; OnPropertyChanged(); }
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

public class SpotLight(
    Point3D position,
    Vector3D direction,
    Color color,
    double range,
    bool on = false,
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
    double falloffMax = 5.0) : ColorViewModel(color, on)
{
    Point3D position = position;
    Vector3D direction = direction;
    double range = range;
    double rangeMin = rangeMin;
    double rangeMax = rangeMax;
    double outerAngle = outerAngle;
    double outerAngleMin = outerAngleMin;
    double outerAngleMax = outerAngleMax;
    double innerAngle = innerAngle;
    double innerAngleMin = innerAngleMin;
    double innerAngleMax = innerAngleMax;
    double falloff = falloff;
    double falloffMin = falloffMin;
    double falloffMax = falloffMax;

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
    public double Range
    {
        get => range;
        set { range = value; OnPropertyChanged(); }
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

public class AmbientLight(Color color, bool on = false) : ColorViewModel(color, on)
{
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

public abstract class ColorViewModel : ViewModel
{
    private Color color;
    private bool on;
    public ICommand PickColorCommand { get; }

    protected ColorViewModel(Color color, bool on = true)
    {
        this.color = color;
        this.on = on;
        PickColorCommand = new RelayCommand(_ => PickColor());
    }

    public Color Color
    {
        get => color;
        set
        {
            color = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(A));
            OnPropertyChanged(nameof(R));
            OnPropertyChanged(nameof(G));
            OnPropertyChanged(nameof(B));
        }
    }

    public byte A
    {
        get => color.A;
        set { Color = Color.FromArgb(value, color.R, color.G, color.B); }
    }
    public byte R
    {
        get => color.R;
        set { Color = Color.FromArgb(color.A, value, color.G, color.B); }
    }
    public byte G
    {
        get => color.G;
        set { Color = Color.FromArgb(color.A, color.R, value, color.B); }
    }
    public byte B
    {
        get => color.B;
        set { Color = Color.FromArgb(color.A, color.R, color.G, value); }
    }

    public bool On
    {
        get => on;
        set { on = value; OnPropertyChanged(); }
    }

    protected virtual void PickColor()
    {
        var dlg = new ColorDialog { Color = System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B) };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            Color = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
        }
    }
}
