using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using PerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
///////////////////////////////////////////////
namespace NeuralNavigator;

sealed class FlyCamera : IDisposable
{
    readonly Viewport3DX m_viewport;
    readonly PerspectiveCamera m_camera;
    readonly HashSet<Key> m_heldKeys = [];

    double m_yaw;
    double m_pitch;
    double m_moveSpeed = 1.0;
    double m_lookSensitivity = 0.08;
    Point m_lastMouse;
    bool m_mouseLooking;
    bool m_mouseDragged;
    bool m_attached;

    const double PitchClamp = 89.0;
    const double MinSpeed = 0.1;
    const double MaxSpeed = 50.0;
    const double SpeedScrollStep = 1.2;
    const double MinFov = 5.0;
    const double MaxFov = 120.0;
    const double FovScrollStep = 3.0;

    public event Action<Point>? HoverMove;
    public event Action<Point>? DoubleClick;
    public event Action<Point>? RightClick;
    public event Action? ResetView;

    public double MoveSpeed
    {
        get => m_moveSpeed;
        set => m_moveSpeed = Math.Clamp(value, MinSpeed, MaxSpeed);
    }

    public FlyCamera(Viewport3DX viewport, PerspectiveCamera camera)
    {
        m_viewport = viewport;
        m_camera = camera;

        Vector3D look = camera.LookDirection;
        m_yaw = Math.Atan2(look.X, -look.Z) * (180.0 / Math.PI);
        m_pitch = Math.Asin(Math.Clamp(look.Y / look.Length, -1, 1)) * (180.0 / Math.PI);

        Attach();
    }

    void Attach()
    {
        if (m_attached) return;
        m_viewport.PreviewKeyDown += OnKeyDown;
        m_viewport.PreviewKeyUp += OnKeyUp;
        m_viewport.PreviewMouseRightButtonDown += OnMouseRightDown;
        m_viewport.PreviewMouseRightButtonUp += OnMouseRightUp;
        m_viewport.PreviewMouseMove += OnMouseMove;
        m_viewport.PreviewMouseWheel += OnMouseWheel;
        m_viewport.MouseDoubleClick += OnDoubleClick;
        m_viewport.LostFocus += OnLostFocus;
        CompositionTarget.Rendering += OnFrame;
        m_attached = true;
    }

    void Detach()
    {
        if (!m_attached) return;
        m_viewport.PreviewKeyDown -= OnKeyDown;
        m_viewport.PreviewKeyUp -= OnKeyUp;
        m_viewport.PreviewMouseRightButtonDown -= OnMouseRightDown;
        m_viewport.PreviewMouseRightButtonUp -= OnMouseRightUp;
        m_viewport.PreviewMouseMove -= OnMouseMove;
        m_viewport.PreviewMouseWheel -= OnMouseWheel;
        m_viewport.MouseDoubleClick -= OnDoubleClick;
        m_viewport.LostFocus -= OnLostFocus;
        CompositionTarget.Rendering -= OnFrame;
        m_attached = false;
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Home)
        {
            ResetView?.Invoke();
            e.Handled = true;
            return;
        }
        if (IsMovementKey(e.Key))
        {
            m_heldKeys.Add(e.Key);
            e.Handled = true;
            m_viewport.Focus();
        }
    }

    void OnKeyUp(object sender, KeyEventArgs e)
    {
        m_heldKeys.Remove(e.Key);
        if (IsMovementKey(e.Key))
            e.Handled = true;
    }

    void OnMouseRightDown(object sender, MouseButtonEventArgs e)
    {
        m_mouseLooking = true;
        m_mouseDragged = false;
        m_lastMouse = e.GetPosition(m_viewport);
        m_viewport.CaptureMouse();
        e.Handled = true;
    }

    void OnMouseRightUp(object sender, MouseButtonEventArgs e)
    {
        bool wasDragging = m_mouseDragged;
        m_mouseLooking = false;
        m_mouseDragged = false;
        m_viewport.ReleaseMouseCapture();

        if (!wasDragging)
            RightClick?.Invoke(e.GetPosition(m_viewport));
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        Point pos = e.GetPosition(m_viewport);

        if (m_mouseLooking)
        {
            double dx = pos.X - m_lastMouse.X;
            double dy = pos.Y - m_lastMouse.Y;
            m_lastMouse = pos;

            if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2)
                m_mouseDragged = true;

            m_yaw += dx * m_lookSensitivity;
            m_pitch -= dy * m_lookSensitivity;
            m_pitch = Math.Clamp(m_pitch, -PitchClamp, PitchClamp);

            UpdateLookDirection();
        }
        else
            HoverMove?.Invoke(pos);
    }

    void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Shift+scroll = adjust move speed (old behavior).
        // Plain scroll = zoom via FOV (telephoto/wide-angle).
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (e.Delta > 0)
                MoveSpeed *= SpeedScrollStep;
            else
                MoveSpeed /= SpeedScrollStep;
        }
        else
        {
            double fov = m_camera.FieldOfView;
            fov += e.Delta > 0 ? -FovScrollStep : FovScrollStep;
            m_camera.FieldOfView = Math.Clamp(fov, MinFov, MaxFov);
        }
        e.Handled = true;
    }

    void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DoubleClick?.Invoke(e.GetPosition(m_viewport));
    }

    void OnLostFocus(object sender, RoutedEventArgs e) => m_heldKeys.Clear();

    void OnFrame(object? sender, EventArgs e)
    {
        if (m_heldKeys.Count == 0) return;

        double yawRad = m_yaw * (Math.PI / 180.0);
        Vector3D forward = new(Math.Sin(yawRad), 0, -Math.Cos(yawRad));
        Vector3D right = new(Math.Cos(yawRad), 0, Math.Sin(yawRad));
        Vector3D up = new(0, 1, 0);
        Vector3D move = new(0, 0, 0);

        if (m_heldKeys.Contains(Key.W)) move += forward;
        if (m_heldKeys.Contains(Key.S)) move -= forward;
        if (m_heldKeys.Contains(Key.D)) move += right;
        if (m_heldKeys.Contains(Key.A)) move -= right;
        if (m_heldKeys.Contains(Key.Space)) move += up;
        if (m_heldKeys.Contains(Key.LeftCtrl) || m_heldKeys.Contains(Key.RightCtrl)) move -= up;

        if (move.Length < 0.001) return;
        move.Normalize();

        Point3D pos = m_camera.Position;
        m_camera.Position = new Point3D(
            pos.X + move.X * m_moveSpeed,
            pos.Y + move.Y * m_moveSpeed,
            pos.Z + move.Z * m_moveSpeed);
    }

    void UpdateLookDirection()
    {
        double yawRad = m_yaw * (Math.PI / 180.0);
        double pitchRad = m_pitch * (Math.PI / 180.0);
        double cosPitch = Math.Cos(pitchRad);

        m_camera.LookDirection = new Vector3D(
            Math.Sin(yawRad) * cosPitch,
            Math.Sin(pitchRad),
            -Math.Cos(yawRad) * cosPitch);
    }

    public void SyncFromCamera()
    {
        Vector3D look = m_camera.LookDirection;
        double len = look.Length;
        if (len < 1e-8) return;
        m_yaw = Math.Atan2(look.X, -look.Z) * (180.0 / Math.PI);
        m_pitch = Math.Asin(Math.Clamp(look.Y / len, -1, 1)) * (180.0 / Math.PI);
    }

    static bool IsMovementKey(Key key) =>
        key is Key.W or Key.A or Key.S or Key.D or Key.Space or Key.LeftCtrl or Key.RightCtrl;

    public void Dispose() => Detach();
}
