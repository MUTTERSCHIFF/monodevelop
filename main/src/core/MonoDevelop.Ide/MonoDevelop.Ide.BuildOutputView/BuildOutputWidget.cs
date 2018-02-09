﻿//
// BuildOuputWidget.cs
//
// Author:
//       Rodrigo Moya <rodrigo.moya@xamarin.com>
//
// Copyright (c) 2017 Microsoft Corp. (http://microsoft.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Xwt;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Ide.Gui.Dialogs;
using MonoDevelop.Components;
using MonoDevelop.Core;
using MonoDevelop.Components.AtkCocoaHelper;
using MonoDevelop.Ide.Gui.Content;

namespace MonoDevelop.Ide.BuildOutputView
{
	class BuildOutputWidget : VBox, IPathedDocument
	{
		TreeView treeView;
		ScrollView scrolledWindow;
		CheckBox showDiagnosticsButton;
		Button saveButton;
		Gtk.VBox box;
		DocumentToolbar toolbar;
		PathBar pathBar;

		public string ViewContentName { get; private set; }
		public BuildOutput BuildOutput { get; private set; }
		public PathEntry [] CurrentPath { get; set; }

		public event EventHandler<string> FileSaved;
		public event EventHandler<DocumentPathChangedEventArgs> PathChanged;

		public BuildOutputWidget (BuildOutput output, string viewContentName)
		{
			Initialize ();
			ViewContentName = viewContentName;
			SetupBuildOutput (output);
		}

		public BuildOutputWidget (FilePath filePath)
		{
			Initialize ();

			ViewContentName = filePath;
			var output = new BuildOutput ();
			output.Load (filePath.FullPath, false);
			SetupBuildOutput (output);
		}

		void SetupBuildOutput (BuildOutput output)
		{
			BuildOutput = output;
			ProcessLogs (false);

			BuildOutput.OutputChanged += (sender, e) => ProcessLogs (showDiagnosticsButton.Active);

			pathBar = new PathBar (this.CreatePathWidget) {
				DrawBottomBorder = false
			};
			var entries = new PathEntry [] {
				new PathEntry (GettextCatalog.GetString ("No selection"))
			};
			UpdatePathBarEntries (entries);
			pathBar.Show ();

			box.PackStart (pathBar, true, true, 10);
			box.ReorderChild (pathBar, 0);
			box.Show ();
		}

		void Initialize ()
		{
			showDiagnosticsButton = new CheckBox (GettextCatalog.GetString ("Diagnostic log verbosity"));
			showDiagnosticsButton.Accessible.Identifier = "BuildOutputWidget.ShowDiagnosticsButton";
			showDiagnosticsButton.TooltipText = GettextCatalog.GetString ("Show full (diagnostics enabled) or reduced log");
			showDiagnosticsButton.Accessible.Description = GettextCatalog.GetString ("Diagnostic log verbosity");
			showDiagnosticsButton.Clicked += (sender, e) => ProcessLogs (showDiagnosticsButton.Active);

			saveButton = new Button (GettextCatalog.GetString ("Save"));
			saveButton.Accessible.Identifier = "BuildOutputWidget.SaveButton";
			saveButton.TooltipText = GettextCatalog.GetString ("Save build output");
			saveButton.Accessible.Description = GettextCatalog.GetString ("Save build output");

			saveButton.Clicked += async (sender, e) => {
				const string binLogExtension = "binlog";
				var dlg = new Gui.Dialogs.OpenFileDialog(GettextCatalog.GetString ("Save as..."), MonoDevelop.Components.FileChooserAction.Save) {
					TransientFor = IdeApp.Workbench.RootWindow,
					InitialFileName = ViewContentName
				};
				if (dlg.Run ()) {
					var outputFile = dlg.SelectedFile;
					if (!outputFile.HasExtension (binLogExtension))
						outputFile = outputFile.ChangeExtension (binLogExtension);
					
					await BuildOutput.Save (outputFile);
					FileSaved?.Invoke (this, outputFile.FileName);
				}
			};

			toolbar = new DocumentToolbar ();

			toolbar.AddSpace ();
			box = new Gtk.VBox ();
			toolbar.Add (box, true);

			toolbar.AddSpace ();
			toolbar.Add (showDiagnosticsButton.ToGtkWidget ());
			toolbar.Add (saveButton.ToGtkWidget ());
			PackStart (toolbar.Container, expand: false, fill: true);

			treeView = new TreeView ();
			treeView.HeadersVisible = false;
			treeView.Accessible.Identifier = "BuildOutputWidget.TreeView";
			treeView.Accessible.Description = GettextCatalog.GetString ("Structured build output");
			treeView.SelectionChanged += TreeView_SelectionChanged;
			var treeColumn = new ListViewColumn {
				CanResize = false,
				Expands = true
			};
			var imageCell = new ImageCellView ();
			var textCell = new TextCellView ();
			treeColumn.Views.Add (imageCell);
			treeColumn.Views.Add (textCell);
			treeView.Columns.Add (treeColumn);

			scrolledWindow = new ScrollView ();
			scrolledWindow.Content = treeView;

			PackStart (scrolledWindow, expand: true, fill: true);
		}

		void IndexChanged (int newIndex)
		{
			if (newIndex >= CurrentPath.Length)
				return;

			if (CurrentPath [newIndex].Tag != null) {
				SelectRow (CurrentPath [newIndex].Tag as BuildOutputNode);
			}
		}

		async void SelectRow (BuildOutputNode node)
		{
			await Runtime.RunInMainThread (() => {
				treeView.SelectRow (node);
			});
		}

		void TreeView_SelectionChanged (object sender, EventArgs e)
		{
			var selectedNode = (sender as Xwt.TreeView).SelectedRow as BuildOutputNode;
			if (selectedNode == null)
				return;

			var stack = new Stack<BuildOutputNode> ();

			stack.Push (selectedNode);
			var parent = selectedNode.Parent;

			while (parent != null) {
				stack.Push (parent);
				parent = parent.Parent;
			}

			var entries = new PathEntry [stack.Count];
			var index = 0;
			while (stack.Count > 0) {
				var node = stack.Pop ();
				var dataSource = treeView.DataSource as BuildOutputDataSource;
				var pathEntry = new PathEntry (dataSource.GetValue (node, 0) as Xwt.Drawing.Image, node.Message);
				pathEntry.Tag = node;
				entries [index] = pathEntry;
				index++;
			}

			UpdatePathBarEntries (entries);
		}

		void UpdatePathBarEntries (PathEntry[] entries)
		{
			pathBar.SetPath (entries);
			this.CurrentPath = pathBar.Path;
		}

		CancellationTokenSource cts;
		static void ExpandChildrenWithErrors (TreeView tree, BuildOutputDataSource dataSource, BuildOutputNode parent)
		{
			int totalChildren = dataSource.GetChildrenCount (parent);
			for (int i = 0; i < totalChildren; i++) {
				var child = dataSource.GetChild (parent, i) as BuildOutputNode;
				if ((child?.HasErrors ?? false)) {
					tree.ExpandToRow (child);
					ExpandChildrenWithErrors (tree, dataSource, child);
				}
			}
		}

		void ProcessLogs (bool showDiagnostics)
		{
			cts?.Cancel ();
			cts = new CancellationTokenSource ();

			Task.Run (async () => {
				await Runtime.RunInMainThread (() => {
					var dataSource = BuildOutput.ToTreeDataSource (showDiagnostics);
					treeView.DataSource = dataSource;
					(treeView.Columns [0].Views [0] as ImageCellView).ImageField = dataSource.ImageField;
					(treeView.Columns [0].Views [1] as TextCellView).MarkupField = dataSource.LabelField;

					// Expand root nodes and nodes with errors
					int rootsCount = dataSource.GetChildrenCount (null);
					for (int i = 0; i < rootsCount; i++) {
						var root = dataSource.GetChild (null, i) as BuildOutputNode;
						treeView.ExpandRow (root, false);
						ExpandChildrenWithErrors (treeView, dataSource, root);
					}
				});
			}, cts.Token);
		}

		int currentIndex = -1;
		public Control CreatePathWidget (int index)
		{
			if (currentIndex != index) {
				IndexChanged (index);
				currentIndex = index;
			}

			PathEntry [] path = CurrentPath;
			if (path == null || index < 0 || index >= path.Length)
				return null;

			var tag = path [index].Tag as BuildOutputNode;
			var window = new DropDownBoxListWindow (new DropDownWindowDataProvider (this, showDiagnosticsButton.Active, tag));
			window.FixedRowHeight = 22;
			window.MaxVisibleRows = 14;
			if (path [index].Tag != null)
				window.SelectItem (path [index].Tag);
			return window;
		}

		class DropDownWindowDataProvider : DropDownBoxListWindow.IListDataProvider
		{
			BuildOutputNode [] list;
			BuildOutputWidget widget;
			BuildOutputDataSource DataSource => widget.treeView.DataSource as BuildOutputDataSource;

			public DropDownWindowDataProvider (BuildOutputWidget widget, bool showDiagnostics, BuildOutputNode node)
			{
				if (widget == null)
					throw new ArgumentNullException ("widget");
				this.widget = widget;
				Reset ();

				var parent = DataSource.GetParent (node) as BuildOutputNode;
				var rootsCount = DataSource.GetChildrenCount (parent);
				list = new BuildOutputNode [rootsCount];
				for (int i = 0; i < rootsCount; i++) {
					list [i] = DataSource.GetChild (parent, i) as BuildOutputNode;
				}
			}

			public int IconCount => list.Length;

			public void ActivateItem (int n) => widget.SelectRow (list [n]);

			public Xwt.Drawing.Image GetIcon (int n) => DataSource.GetValue (list [n], 0) as Xwt.Drawing.Image;

			public string GetMarkup (int n) => list [n].Message;

			public object GetTag (int n) => list [n];

			public void Reset () => list = new BuildOutputNode [0];
		}
	}
}
