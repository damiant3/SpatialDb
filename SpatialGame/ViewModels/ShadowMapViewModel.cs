using System.Windows;

namespace SpatialGame.ViewModels;

public class ShadowMapViewModel : ViewModel
{
    private double distance;
    private double bias;
    private double intensity;
    private bool isSceneDynamic;
    private object? lightCamera;
    private Size resolution;

    public ShadowMapViewModel()
    {
        Distance = 2000;
        Bias = 0.0005;
        Intensity = 1.0;
        IsSceneDynamic = true;
        Resolution = new Size(2048, 2048);
    }
    public double Distance
    {
        get => distance;
        set { distance = value; OnPropertyChanged(); }
    }
    public double Bias
    {
        get => bias;
        set { bias = value; OnPropertyChanged(); }
    }
    public double Intensity
    {
        get => intensity;
        set { intensity = value; OnPropertyChanged(); }
    }
    public bool IsSceneDynamic
    {
        get => isSceneDynamic;
        set { isSceneDynamic = value; OnPropertyChanged(); }
    }
    public object? LightCamera
    {
        get => lightCamera;
        set { lightCamera = value; OnPropertyChanged(); }
    }
    public Size Resolution
    {
        get => resolution;
        set { resolution = value; OnPropertyChanged(); }
    }

}
