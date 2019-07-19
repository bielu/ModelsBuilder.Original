﻿using System;
using System.ComponentModel.Design;
using System.IO;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using ZpqrtBnk.ModelsBuilder.Extension.VisualStudio;
using Task = System.Threading.Tasks.Task;

namespace ZpqrtBnk.ModelsBuilder.Extension
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class BuildModelsCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("84e96047-2ab3-4a6b-bbbb-ccc65b541fbe");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly ExtensionPackage _package;

        /// <summary>
        /// Project item that supports the command.
        /// </summary>
        private ProjectItem _item;

        /// <summary>
        /// Initializes a new instance of the <see cref="BuildModelsCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private BuildModelsCommand(ExtensionPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new OleMenuCommand(Execute, menuCommandID);
            menuItem.BeforeQueryStatus += BeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static BuildModelsCommand Instance { get; private set; }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider => _package;

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(ExtensionPackage package)
        {
            // Switch to the main thread - the call to AddCommand in Command1's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new BuildModelsCommand(package, commandService);
        }

        private void BeforeQueryStatus(object sender, EventArgs e)
        {
            var button = (OleMenuCommand)sender;
            button.Visible = button.Enabled = false;

            _item = VisualStudioHelper.GetProjectItem(_package.Dte);

            if (_item == null || _item.ContainingProject == null || _item.Properties == null)
                return;

            var inputFile = _item.Properties.Item("FullPath").Value.ToString();
            var extension = Path.GetExtension(inputFile);

            button.Visible = button.Enabled = (extension == ".mb");
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            /*
            string message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.MenuItemCallback()", this.GetType().FullName);
            string title = "BuildModelsCommand";

            // Show a message box to prove we were here
            VsShellUtilities.ShowMessageBox(
                this._package,
                message,
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            */

            // on the very first run, _item can be null?!
            var item = _item ?? VisualStudioHelper.GetProjectItem(_package.Dte);

            // does *not* throw, handles its own errors
            // including if item is null
            Generator.Generate(_package, item);
        }
    }
}
