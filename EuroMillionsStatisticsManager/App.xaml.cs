using System.Windows;

namespace EuroMillionsStatisticsManager
{
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App: Application
  {
    protected override void OnStartup(StartupEventArgs e)
    {
      base.OnStartup(e);
      ShutdownMode = ShutdownMode.OnExplicitShutdown;

      var pleaseWait = new PleaseWait();
      pleaseWait.ContentRendered += (_, _) =>
      {
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        pleaseWait.Close();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
      };

      pleaseWait.Show();
    }
  }

}
