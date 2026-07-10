using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using TorrentFree.Services;
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
	private readonly DispatcherQueue? _dispatcherQueue;
	private readonly TaskCompletionSource<bool> _servicesReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private AppActivationArguments? _initialActivation;

	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();

		_dispatcherQueue = DispatcherQueue.GetForCurrentThread();
		var currentInstance = AppInstance.GetCurrent();
		var initialActivation = currentInstance.GetActivatedEventArgs();
		_initialActivation = initialActivation;
		_mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
		if (!_mainInstance.IsCurrent)
		{
			var redirectCompleted = false;
			try
			{
				var redirectOperation = _mainInstance.RedirectActivationToAsync(initialActivation);
				redirectCompleted = WaitForRedirectCompletion(redirectOperation);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Activation redirection failed: {ex}");
			}
			if (!redirectCompleted)
			{
				System.Diagnostics.Debug.WriteLine("Activation redirection did not complete; continuing without exit to avoid losing activation.");
				return;
			}
			Environment.Exit(0);
			return;
		}

		_mainInstance.Activated += OnActivated;

		// The Activated event is raised for activations redirected from later
		// processes. The cold-start arguments are dispatched from CreateMauiApp,
		// after the service provider has been built.
	}

	protected override MauiApp CreateMauiApp()
	{
		try
		{
			var app = MauiProgram.CreateMauiApp();
			_servicesReady.TrySetResult(true);

			var initialActivation = Interlocked.Exchange(ref _initialActivation, null);
			if (initialActivation is not null)
			{
				OnActivated(AppInstance.GetCurrent(), initialActivation);
			}

			return app;
		}
		catch (Exception ex)
		{
			_servicesReady.TrySetException(ex);
			throw;
		}
	}

	private void OnActivated(object? sender, AppActivationArguments args)
	{
		var paths = ExtractTorrentPaths(args);
		if (paths.Count == 0)
		{
			return;
		}

		DispatchActivation(paths);
	}

	private void DispatchActivation(IReadOnlyList<string> paths)
	{
		if (_dispatcherQueue?.HasThreadAccess == true)
		{
			_ = ProcessActivationPathsSafelyAsync(paths);
			return;
		}

		if (_dispatcherQueue?.TryEnqueue(async () => await ProcessActivationPathsSafelyAsync(paths)) == true)
		{
			return;
		}

		System.Diagnostics.Debug.WriteLine("Unable to dispatch activation to the WinUI UI thread.");
	}

	private async Task ProcessActivationPathsSafelyAsync(IReadOnlyList<string> paths)
	{
		try
		{
			await ProcessActivationPathsAsync(paths);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Activation import failed: {ex}");
		}
	}

	private async Task ProcessActivationPathsAsync(IReadOnlyList<string> paths)
	{
		await _servicesReady.Task;
		var viewModel = MauiProgram.Services.GetService<MainViewModel>();
		if (viewModel is null)
		{
			throw new InvalidOperationException("The main view model is unavailable for file activation.");
		}

		await ActivationImportCoordinator.ImportAsync(
			paths,
			viewModel.EnsureInitializedAsync,
			viewModel.ImportTorrentFileFromPathAsync);

		ActivateMainWindow();
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

	private static bool WaitForRedirectCompletion(object? redirectOperation)
	{
		if (redirectOperation is null)
		{
			return true;
		}

		const int initialTimeoutMs = 5000;
		const int fallbackTimeoutMs = 25000;

		try
		{
			if (redirectOperation is Task task)
			{
				return WaitWithTimeouts(task, initialTimeoutMs, fallbackTimeoutMs);
			}

			if (redirectOperation is IAsyncAction asyncAction)
			{
				return WaitWithTimeouts(asyncAction.AsTask(), initialTimeoutMs, fallbackTimeoutMs);
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Activation redirection wait failed: {ex}");
		}

		return false;
	}

	private static bool WaitWithTimeouts(Task task, int initialTimeoutMs, int fallbackTimeoutMs)
	{
		if (task.Wait(initialTimeoutMs))
		{
			return true;
		}

		System.Diagnostics.Debug.WriteLine($"Activation redirection did not complete within {initialTimeoutMs / 1000} seconds; waiting up to {fallbackTimeoutMs / 1000} seconds longer.");

		if (task.Wait(fallbackTimeoutMs))
		{
			return true;
		}

		System.Diagnostics.Debug.WriteLine("Activation redirection still pending; waiting for completion to avoid dropping activation.");
		task.Wait();
		return true;
	}
}
