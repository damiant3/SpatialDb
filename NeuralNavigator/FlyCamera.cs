using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using PerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
///////////////////////////////////////////////
namespace NeuralNavigator;

/// <summary>
/// WASD + mouse-look camera controller for video-game-style 3D navigation.
/// Hooks into WPF input events and drives smooth per-frame camera updates
/// via CompositionTarget.Rendering.
/// </summary>
sealed class FlyCamera : IDisposable
{
    readonly Viewport3DX m_viewport;
    readonly PerspectiveCamera m_camera;
    readonly HashSet<Key> m_heldKeys = [];

    double m_yaw;
    double m_pitch;
    double m_moveSpeed = 1.0;
    double m_lookSensitivity = 0.2;
    Point m_lastMouse;
    bool m_mouseLooking;
    bool m_attached;

    const double PitchClamp = 89.0;
    const double MinSpeed = 0.1;
    const double MaxSpeed = 50.0;
    const double SpeedScrollStep = 1.2;

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
        m_viewport.LostFocus -= OnLostFocus;
        CompositionTarget.Rendering -= OnFrame;
        m_attached = false;
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
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
        m_lastMouse = e.GetPosition(m_viewport);
        m_viewport.CaptureMouse();
        e.Handled = true;
    }

    void OnMouseRightUp(object sender, MouseButtonEventArgs e)
    {
        m_mouseLooking = false;
        m_viewport.ReleaseMouseCapture();
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!m_mouseLooking) return;

        Point pos = e.GetPosition(m_viewport);
        double dx = pos.X - m_lastMouse.X;
        double dy = pos.Y - m_lastMouse.Y;
        m_lastMouse = pos;

        m_yaw += dx * m_lookSensitivity;
        m_pitch -= dy * m_lookSensitivity;
        m_pitch = Math.Clamp(m_pitch, -PitchClamp, PitchClamp);

        UpdateLookDirection();
    }

    void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0)
            MoveSpeed *= SpeedScrollStep;
        else
            MoveSpeed /= SpeedScrollStep;
        e.Handled = true;
    }

    void OnLostFocus(object sender, RoutedEventArgs e) => m_heldKeys.Clear();

    void OnFrame(object? sender, EventArgs e)
    {
        if (m_heldKeys.Count == 0) return;

        double yawRad = m_yaw * (Math.PI / 180.0);
        double pitchRad = m_pitch * (Math.PI / 180.0);

        // Forward = where the camera is looking (horizontal plane for WASD)
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

    static bool IsMovementKey(Key key) =>
        key is Key.W or Key.A or Key.S or Key.D or Key.Space or Key.LeftCtrl or Key.RightCtrl;

    public void Dispose() => Detach();
}
