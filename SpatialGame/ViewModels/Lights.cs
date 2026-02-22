namespace SpatialGame.ViewModels
{
    using System.ComponentModel;
    using System.Runtime.CompilerServices;
    using System.Windows.Media;
    using System.Windows.Media.Media3D;

    public class DirectionalLight(Vector3D direction, Color color, bool on = true) : ViewModel
    {
        Vector3D direction = direction;
        Color color = color;
        bool on = on;

        public Vector3D Direction
        {
            get => direction;
            set { direction = value; OnPropertyChanged(nameof(Direction)); }
        }
        public double DirectionX
        {
            get => direction.X;
            set { direction.X = value; OnPropertyChanged(nameof(DirectionX)); OnPropertyChanged(nameof(Direction)); }
        }
        public double DirectionY
        {
            get => direction.Y;
            set { direction.Y = value; OnPropertyChanged(nameof(DirectionY)); OnPropertyChanged(nameof(Direction)); }
        }
        public double DirectionZ
        {
            get => direction.Z;
            set { direction.Z = value; OnPropertyChanged(nameof(DirectionZ)); OnPropertyChanged(nameof(Direction)); }
        }
        public Color Color
        {
            get => color;
            set { color = value; OnPropertyChanged(nameof(Color)); }
        }
        public bool On
        {
            get => on;
            set { on = value; OnPropertyChanged(nameof(On)); }
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
        public PointLight(Point3D position, Color color, double range, bool on = false, double rangeMin = 10, double rangeMax = 1000)
        {
            this.position = position;
            this.color = color;
            this.range = range;
            this.on = on;
            this.rangeMin = rangeMin;
            this.rangeMax = rangeMax;
        }
        public Point3D Position
        {
            get => position;
            set { position = value; OnPropertyChanged(nameof(Position)); }
        }
        public double PositionX
        {
            get => position.X;
            set { position.X = value; OnPropertyChanged(nameof(PositionX)); OnPropertyChanged(nameof(Position)); }
        }
        public double PositionY
        {
            get => position.Y;
            set { position.Y = value; OnPropertyChanged(nameof(PositionY)); OnPropertyChanged(nameof(Position)); }
        }
        public double PositionZ
        {
            get => position.Z;
            set { position.Z = value; OnPropertyChanged(nameof(PositionZ)); OnPropertyChanged(nameof(Position)); }
        }
        public Color Color
        {
            get => color;
            set { color = value; OnPropertyChanged(nameof(Color)); }
        }
        public double Range
        {
            get => range;
            set { range = value; OnPropertyChanged(nameof(Range)); }
        }
        public bool On
        {
            get => on;
            set { on = value; OnPropertyChanged(nameof(On)); }
        }
        public double RangeMin
        {
            get => rangeMin;
            set { rangeMin = value; OnPropertyChanged(nameof(RangeMin)); }
        }
        public double RangeMax
        {
            get => rangeMax;
            set { rangeMax = value; OnPropertyChanged(nameof(RangeMax)); }
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
        public SpotLight(Point3D position, Vector3D direction, Color color, double range, bool on = true, double rangeMin = 10, double rangeMax = 2000, double outerAngle = 60, double outerAngleMin = 10, double outerAngleMax = 120, double innerAngle = 30, double innerAngleMin = 0, double innerAngleMax = 120, double falloff = 1.0, double falloffMin = 0.1, double falloffMax = 5.0)
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
        }
        public Point3D Position
        {
            get => position;
            set { position = value; OnPropertyChanged(nameof(Position)); }
        }
        public double PositionX
        {
            get => position.X;
            set { position.X = value; OnPropertyChanged(nameof(PositionX)); OnPropertyChanged(nameof(Position)); }
        }
        public double PositionY
        {
            get => position.Y;
            set { position.Y = value; OnPropertyChanged(nameof(PositionY)); OnPropertyChanged(nameof(Position)); }
        }
        public double PositionZ
        {
            get => position.Z;
            set { position.Z = value; OnPropertyChanged(nameof(PositionZ)); OnPropertyChanged(nameof(Position)); }
        }
        public Vector3D Direction
        {
            get => direction;
            set { direction = value; OnPropertyChanged(nameof(Direction)); }
        }
        public double DirectionX
        {
            get => direction.X;
            set { direction.X = value; OnPropertyChanged(nameof(DirectionX)); OnPropertyChanged(nameof(Direction)); }
        }
        public double DirectionY
        {
            get => direction.Y;
            set { direction.Y = value; OnPropertyChanged(nameof(DirectionY)); OnPropertyChanged(nameof(Direction)); }
        }
        public double DirectionZ
        {
            get => direction.Z;
            set { direction.Z = value; OnPropertyChanged(nameof(DirectionZ)); OnPropertyChanged(nameof(Direction)); }
        }
        public Color Color
        {
            get => color;
            set { color = value;  OnPropertyChanged(nameof(Color)); }
        }
        public double Range
        {
            get => range;
            set { range = value; OnPropertyChanged(nameof(Range)); }
        }
        public bool On
        {
            get => on;
            set { on = value; OnPropertyChanged(nameof(On)); }
        }
        public double RangeMin
        {
            get => rangeMin;
            set { rangeMin = value; OnPropertyChanged(nameof(RangeMin)); }
        }
        public double RangeMax
        {
            get => rangeMax;
            set { rangeMax = value; OnPropertyChanged(nameof(RangeMax)); }
        }
        public double OuterAngle
        {
            get => outerAngle;
            set { outerAngle = value; OnPropertyChanged(nameof(OuterAngle)); }
        }
        public double OuterAngleMin
        {
            get => outerAngleMin;
            set { outerAngleMin = value; OnPropertyChanged(nameof(OuterAngleMin)); }
        }
        public double OuterAngleMax
        {
            get => outerAngleMax;
            set { outerAngleMax = value; OnPropertyChanged(nameof(OuterAngleMax)); }
        }
        public double InnerAngle
        {
            get => innerAngle;
            set { innerAngle = value; OnPropertyChanged(nameof(InnerAngle)); }
        }
        public double InnerAngleMin
        {
            get => innerAngleMin;
            set { innerAngleMin = value; OnPropertyChanged(nameof(InnerAngleMin)); }
        }
        public double InnerAngleMax
        {
            get => innerAngleMax;
            set { innerAngleMax = value; OnPropertyChanged(nameof(InnerAngleMax)); }
        }
        public double Falloff
        {
            get => falloff;
            set { falloff = value; OnPropertyChanged(nameof(Falloff)); }
        }
        public double FalloffMin
        {
            get => falloffMin;
            set { falloffMin = value; OnPropertyChanged(nameof(FalloffMin)); }
        }
        public double FalloffMax
        {
            get => falloffMax;
            set { falloffMax = value; OnPropertyChanged(nameof(FalloffMax)); }
        }
    }

    public abstract class ViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
