using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using TorrentFree.ViewModels;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TorrentFree.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	private const string InstanceKey = "TorrentFreeMain";
	private readonly AppInstance _mainInstance;

	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();

		_mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
		if (!_mainInstance.IsCurrent)
		{
			try
			{
				var redirectOperation = _mainInstance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
				WaitForRedirectCompletion(redirectOperation);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Activation redirection failed: {ex}");
			}
			Environment.Exit(0);
			return;
		}

		_mainInstance.Activated += OnActivated;
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	private async void OnActivated(object? sender, AppActivationArguments args)
	{
		var paths = ExtractTorrentPaths(args);
		if (paths.Count == 0)
		{
			return;
		}

		await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			var viewModel = MauiProgram.Services.GetService<MainViewModel>();
			if (viewModel is null)
			{
				return;
			}

			foreach (var path in paths)
			{
				await viewModel.ImportTorrentFileFromPathAsync(path);
			}

			ActivateMainWindow();
		});
	}

	private static IReadOnlyList<string> ExtractTorrentPaths(AppActivationArguments args)
	{
		var paths = new List<string>();

		if (args.Data is IFileActivatedEventArgs fileArgs)
		{
			foreach (var file in fileArgs.Files.OfType<IStorageFile>())
			{
				if (IsTorrentPath(file.Path))
				{
					paths.Add(file.Path);
				}
			}
		}
		else if (args.Data is ILaunchActivatedEventArgs launchArgs)
		{
			foreach (var arg in ParseArguments(launchArgs.Arguments))
			{
				if (IsTorrentPath(arg))
				{
					paths.Add(arg);
				}
			}
		}

		return paths;
	}

	private static IEnumerable<string> ParseArguments(string? commandLine)
	{
		if (string.IsNullOrWhiteSpace(commandLine))
		{
			yield break;
		}

		var builder = new StringBuilder();
		var inQuotes = false;
		foreach (var ch in commandLine)
		{
			if (ch == '"')
			{
				inQuotes = !inQuotes;
				continue;
			}

			if (char.IsWhiteSpace(ch) && !inQuotes)
			{
				if (builder.Length > 0)
				{
					yield return builder.ToString();
					builder.Clear();
				}
				continue;
			}

			builder.Append(ch);
		}

		if (builder.Length > 0)
		{
			yield return builder.ToString();
		}
	}

	private static bool IsTorrentPath(string? path)
	{
		return !string.IsNullOrWhiteSpace(path) && path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);
	}

	private static void ActivateMainWindow()
	{
		var window = Microsoft.Maui.Controls.Application.Current?.Windows?.FirstOrDefault();
		if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window platformWindow)
		{
			platformWindow.Activate();
		}
	}

	private static void WaitForRedirectCompletion(object? redirectOperation)
	{
		if (redirectOperation is null)
		{
			return;
		}

		if (redirectOperation is Task task)
		{
			task.GetAwaiter().GetResult();
			return;
		}

		if (redirectOperation is IAsyncAction asyncAction)
		{
			asyncAction.AsTask().GetAwaiter().GetResult();
		}
	}
}

