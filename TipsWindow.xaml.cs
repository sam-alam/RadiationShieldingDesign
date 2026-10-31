using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RadiationShieldingDesign
{
    /// <summary>
    /// Interaction logic for TipsWindow.xaml
    /// </summary>
    public partial class TipsWindow : Window
    {
        public TipsWindow()
        {
            InitializeComponent();
        }
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

    }
}
