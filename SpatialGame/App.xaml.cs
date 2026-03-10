using SpatialGame.ViewModels;
using System.Configuration;
using System.Data;
using System.Windows;

namespace SpatialGame
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            XkcdColors.ReadFileAndOutputProperties();
        }
    }

}
