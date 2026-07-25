using System.Windows;
using JudasEncodingManager.Converters;
using JudasEncodingManager.ViewModels;

namespace JudasEncodingManager
{
    public partial class AniDLSearchWindow : Window
    {
        public AniDLSearchWindow(AniDLSearchViewModel viewModel)
        {
            // Register converters referenced in AniDLSearchWindow.xaml
            Resources.Add("BoolToVisibilityConverter",        new BoolToVisibilityConverter());
            Resources.Add("InverseBoolToVisibilityConverter", new InverseBoolToVisibilityConverter());
            Resources.Add("BoolToColorConverter",             new BoolToColorConverter());
            Resources.Add("StringToVisibilityConverter",      new StringToVisibilityConverter());
            Resources.Add("NullToBoolConverter",              new NullToBoolConverter());

            InitializeComponent();
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
