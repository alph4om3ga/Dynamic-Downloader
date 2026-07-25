using System.Windows;
using System.Windows.Input;
using JudasEncodingManager.Converters;
using JudasEncodingManager.ViewModels;

namespace JudasEncodingManager
{
    public partial class CrunchyrollSearchWindow : Window
    {
        private readonly CrunchyrollSearchViewModel _vm;

        public CrunchyrollSearchWindow(CrunchyrollSearchViewModel vm)
        {
            // Register converters this window uses
            Resources.Add("BoolToSearchLabelConverter",  new BoolToSearchLabelConverter());
            Resources.Add("StringToVisibilityConverter", new StringToVisibilityConverter());

            InitializeComponent();
            DataContext = _vm = vm;

            // Double-click a result to apply immediately
            ResultsList.MouseDoubleClick += (_, _) => ApplySelected();

            Loaded += (_, _) => SearchBox.Focus();
        }

        private void ApplySelected()
        {
            if (_vm.SelectedSeries == null) return;
            _vm.SelectCommand.Execute(null);
            if (_vm.DialogResult)
                DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
