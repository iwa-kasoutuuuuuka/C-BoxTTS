using System;
using System.Windows;

namespace CBoxTTS.Native
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Normal Startup: Programmatically instantiate and show MainWindow
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }
}
