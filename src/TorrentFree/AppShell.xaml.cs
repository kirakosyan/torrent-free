namespace TorrentFree;

public partial class AppShell : Shell
{
    public AppShell(MainPage mainPage)
    {
        InitializeComponent();
        
        // Register the main page for Shell navigation
        Routing.RegisterRoute("MainPage", typeof(MainPage));
        
        // Set the main page as the content
        var shellContent = (ShellContent)Items[0].Items[0].Items[0];
        shellContent.Content = mainPage;
    }
}
