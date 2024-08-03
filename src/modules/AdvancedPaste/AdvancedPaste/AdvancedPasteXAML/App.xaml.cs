﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvancedPaste.Helpers;
using AdvancedPaste.Settings;
using AdvancedPaste.ViewModels;
using ManagedCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinUIEx;
using static AdvancedPaste.Helpers.NativeMethods;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.
namespace AdvancedPaste
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application, IDisposable
    {
        public IHost Host { get; private set; }

        private MainWindow window;

        private nint windowHwnd;

        private OptionsViewModel viewModel;

        private bool disposedValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="App"/> class.
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();

            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder().UseContentRoot(AppContext.BaseDirectory).ConfigureServices((context, services) =>
            {
                services.AddSingleton<OptionsViewModel>();
                services.AddSingleton<IUserSettings, UserSettings>();
            }).Build();

            viewModel = GetService<OptionsViewModel>();

            UnhandledException += App_UnhandledException;
        }

        public MainWindow GetMainWindow()
        {
            return window;
        }

        public static T GetService<T>()
            where T : class
        {
            if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
            {
                throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
            }

            return service;
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs?.Length > 1)
            {
                if (int.TryParse(cmdArgs[1], out int powerToysRunnerPid))
                {
                    RunnerHelper.WaitForPowerToysRunner(powerToysRunnerPid, () =>
                    {
                        Environment.Exit(0);
                    });
                }
            }

            if (cmdArgs?.Length > 2)
            {
                ProcessNamedPipe(cmdArgs[2]);
            }
        }

        private void ProcessNamedPipe(string pipeName)
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            var connectTimeout = TimeSpan.FromSeconds(10);
            Action<string> messageHandler = message => dispatcherQueue.TryEnqueue(() => OnNamedPipeMessage(message));

            Task.Run(async () =>
            {
                await NamedPipeProcessor.ProcessNamedPipeAsync(pipeName, connectTimeout, messageHandler, CancellationToken.None);
            });
        }

        private void OnNamedPipeMessage(string message)
        {
            var messageParts = message.Split();
            var messageType = messageParts.First();

            if (messageType == interop.Constants.AdvancedPasteShowUIMessage())
            {
                OnAdvancedPasteHotkey();
            }
            else if (messageType == interop.Constants.AdvancedPasteMarkdownMessage())
            {
                OnAdvancedPasteMarkdownHotkey();
            }
            else if (messageType == interop.Constants.AdvancedPasteJsonMessage())
            {
                OnAdvancedPasteJsonHotkey();
            }
            else if (messageType == interop.Constants.AdvancedPasteCustomActionMessage())
            {
                OnAdvancedPasteCustomActionHotkey(messageParts);
            }
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Logger.LogError("Unhandled exception", e.Exception);
        }

        private void OnAdvancedPasteJsonHotkey()
        {
            viewModel.GetClipboardData();
            viewModel.ToJsonFunction(true);
        }

        private void OnAdvancedPasteMarkdownHotkey()
        {
            viewModel.GetClipboardData();
            viewModel.ToMarkdownFunction(true);
        }

        private void OnAdvancedPasteHotkey()
        {
            viewModel.OnShow();

            if (window is null)
            {
                window = new MainWindow();
                windowHwnd = window.GetWindowHandle();

                MoveWindowToActiveMonitor();

                window.Activate();
            }
            else
            {
                MoveWindowToActiveMonitor();

                Windows.Win32.PInvoke.ShowWindow((Windows.Win32.Foundation.HWND)windowHwnd, Windows.Win32.UI.WindowsAndMessaging.SHOW_WINDOW_CMD.SW_SHOW);
                WindowHelpers.BringToForeground(windowHwnd);
            }

            window.SetFocus();
        }

        private void OnAdvancedPasteCustomActionHotkey(string[] messageParts)
        {
            if (messageParts.Length != 2)
            {
                Logger.LogWarning("Unexpected custom action message");
            }
            else
            {
                viewModel.GetClipboardData();

                if (!int.TryParse(messageParts[1], CultureInfo.InvariantCulture, out int id))
                {
                    Logger.LogWarning($"Unexpected custom action message id {messageParts[1]}");
                }
                else
                {
                    viewModel.ExecuteCustomAction(id, true);
                }
            }
        }

        private void MoveWindowToActiveMonitor()
        {
            if (GetCursorPos(out PointInter cursorPosition))
            {
                DisplayArea displayArea = DisplayArea.GetFromPoint(new PointInt32(cursorPosition.X, cursorPosition.Y), DisplayAreaFallback.Nearest);

                var x = displayArea.WorkArea.X + (displayArea.WorkArea.Width / 2) - (window.Width / 2);
                var y = displayArea.WorkArea.Y + (displayArea.WorkArea.Height / 2) - (window.Height / 2);

                window.MoveAndResize(x, y, window.Width, window.Height);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    window.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
