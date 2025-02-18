using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static BookTool.Error;
using static BookTool.Patch;
using Path = System.IO.Path;
namespace BookTool;

public partial class MainWindow : Window {
   public MainWindow () {
      InitializeComponent ();
      mGTLCommand = new RelayCommand (GoToLine, ContentLoaded);
      InputBindings.Add (new KeyBinding (mGTLCommand, mGTLGesture));
      mRunHeight = (int)new FormattedText ("9999", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface (mENDoc.FontFamily.ToString ()), mENDoc.FontSize, Brushes.Black, null, 1).Height;
      Loaded += delegate {
         mScroller = (ScrollViewer)((Decorator)VisualTreeHelper.GetChild (VisualTreeHelper.GetChild (mLangDocScroll, 0), 0)).Child;
      };
   }
   readonly RelayCommand mGTLCommand;
   readonly KeyGesture mGTLGesture = new (Key.G, ModifierKeys.Control);

   #region Implmentation --------------------------------------------
   /// <summary>Returns true if FlowDocument content has been loaded.</summary>
   // Using a get-only method instead of property so it can be passed
   // to the RelayCommand Constructor as a canExecute condition
   static bool ContentLoaded () => mContentLoaded;
   static bool mContentLoaded;

   /// <summary>Scrolls to line number</summary>
   public void ScrollTo (int pos) => mScroller?.ScrollToVerticalOffset (pos * mRunHeight);
   ScrollViewer? mScroller;
   readonly int mRunHeight;

   void GenerateandProcessPatch () {
      Error err;
      err = Generate ();
      UpdateDoc (mENDoc, err);
      if (Target is null) return;
      UpdateDoc (mLangDoc, ProcessPatch ());
   }

   void ResetRep () {
      RunHiddenCommandLineApp ("git.exe", $"restore .", out _, workingdir: Target?.Path);
   }

   void Reset () {
      UpdateDoc (mLangDoc, Clear); UpdateDoc (mENDoc, Clear);
      PatchFile?.Clear ();
      if (File.Exists ($"{Target?.Path}/change1.DE.patch")) File.Delete ($"{Target?.Path}/change1.DE.patch");
   }

   void UpdateDoc (FlowDocument doc, Error err) {
      doc.Blocks.Clear ();
      var para = new Paragraph () { KeepTogether = true, TextAlignment = TextAlignment.Left };
      switch (err) {
         case OK:
            if (PatchFile is null) break;
            PatchFile.ForEach (line => para.Inlines.Add (new Run ($"{line}\n") {
               Background = line[0] is '+' ? Brushes.GreenYellow :
                            line[0] is '-' ? Brushes.Red :
                            Brushes.Transparent
            })); mContentLoaded = true; mMaxRows = PatchFile.Count + 1; break;
         case Clear: mContentLoaded = false; break;
         default: para.Inlines.Add (new Run ($"Error: {err}\n")); Errors.ForEach (x => para.Inlines.Add (new Run ($"{x}\n"))); Errors.Clear (); mContentLoaded = true; break;
      }
      doc.Blocks.Add (para);
   }
   #endregion

   #region WPF Events -----------------------------------------------
   void OnMouseWheel (object sender, MouseWheelEventArgs e)
      => mLeftTreeScroll.ScrollToVerticalOffset (-e.Delta + mLeftTreeScroll.VerticalOffset);

   void OnSelectionChanged (object sender, RoutedPropertyChangedEventArgs<object> e) {
      if (sender is not TreeView tv || tv.SelectedItem is not string selected) return;
      CommitID = selected.Split ("  ")[0];
      Reset ();
      GenerateandProcessPatch ();
   }

   /// <summary>Navigates to user entered line number.</summary>
   void GoToLine () {
      mGoToLine = new GoToLine (mMaxRows) { Owner = this };
      if (mGoToLine.ShowDialog () is true) ScrollTo (mGoToLine.LineNumber);
   }
   int mMaxRows;
   GoToLine? mGoToLine;

   void OnOpenClicked (object sender, RoutedEventArgs e) {
      if (sender is not Button btn) return;
      OpenFolderDialog fd = new () { Multiselect = false, DefaultDirectory = "C:" };
      if (fd.ShowDialog () != true) return;
      bool setSource = btn.Name == "mOpenEN";
      SetRep (setSource);
      if (setSource) UpdateDoc (mENDoc, OK);
      else UpdateDoc (mLangDoc, OK);

      // Helper Methods ---------------------------------------------
      Error SetRep (bool source) {
         string folderPath = fd.FolderName;
         if (Directory.GetDirectories (folderPath, ".git", SearchOption.TopDirectoryOnly).Length == 0) return SelectedFolderIsNotARepository;
         var results = RunHiddenCommandLineApp ("git.exe", $"branch", out _, workingdir: folderPath);
         Repository rep = new (folderPath, results.Select (a => a.ToString ()).First (a => a.Contains ("main") || a.Contains ("master")).Trim ('*'));
         if (source) {
            Reset ();
            Source = rep;
            mSelectedRep.Content = folderPath;
            mCommitTree.ItemsSource = null;
            mCommitTree.ItemsSource = GetRecentCommits (Source);
         } else {
            Target = rep;
            mTargetRep.Content = folderPath;
            mFileTree.ItemsSource = null; mFileTree.Items.Clear ();
            mFileTree.ItemsSource = GetRecentCommits (Target);
            if (PatchFile is not null) UpdateDoc (mLangDoc, ProcessPatch ());
         }
         return OK;
      }

      List<string> GetRecentCommits (Repository rep) {
         RunHiddenCommandLineApp ("git.exe", $"switch {rep.Main}", out _, workingdir: rep.Path);
         return RunHiddenCommandLineApp ("git.exe", "log -n 30 --format=\"%h  %s\"", out _, workingdir: rep.Path);
      }
   }

   void OnClickExport (object sender, RoutedEventArgs e) => SavePatchInTargetRep ();

   void OnClickImport (object sender, RoutedEventArgs e) => LoadPatchFileFromRep ();

   void OnClickApply (object sender, RoutedEventArgs e) {
      var err = Apply ();
      UpdateDoc (mLangDoc, err);
      if (err == OK) PopulateTreeView ();
      else mFileTree.ItemsSource = null;
   }

   /// <summary>Populates Treeview with underlying directories and files.</summary>
   void PopulateTreeView () {
      mFileTree.ItemsSource = null;
      var info = new DirectoryInfo (Target!.Path);
      var dir = info.GetDirectories ().Where (a => !a.Name.StartsWith ('.'));
      foreach (var folder in dir) {
         var item = MakeTVI (folder.Name);
         if (Path.GetDirectoryName (folder.Name) is string path && !string.IsNullOrWhiteSpace (path)) AddSubItems (path, item);
         if (item.HasItems) mFileTree.Items.Add (item);
      }
      var item2 = MakeTVI (info.Name, expanded: true);
      AddSubItems (Target.Path, item2);
      if (item2.HasItems) mFileTree.Items.Add (item2);
   }

   /// <summary>Adds Sub-Directories and Files from a given file path as per specified pattern</summary>
   /// <param name="pattern">SearchPattern for file, if any.</param>
   void AddSubItems (string path, TreeViewItem item, string pattern = "", bool expanded = false) {
      var dirInfo = new DirectoryInfo (path);
      var dirs = dirInfo.GetDirectories ().Where (a => !a.Name.StartsWith ('.'));
      foreach (var directory in dirs) {
         var tvi = MakeTVI (directory.Name, directory.FullName, expanded);
         AddSubItems (directory.FullName, tvi, pattern);
         if (tvi.HasItems) item.Items.Add (tvi);
      }
      var files = dirInfo.GetFiles (pattern);
      foreach (var file in files)
         item.Items.Add (MakeTVI (file.Name, file.FullName));
   }

   // Helper method to return TreeViewItem with given parameters with OnSelected, OnTreeViewExpanded Handlers attached.
   TreeViewItem MakeTVI (string header, string? tag = null, bool expanded = false) {
      var tvi = new TreeViewItem () { Header = header, Tag = tag, IsExpanded = expanded };
      tvi.Selected += OnSelected;
      return tvi;
   }

   void OnSelected (object sender, RoutedEventArgs e) {
      if (sender is not TreeViewItem tv) return;
      if (tv.HasItems) tv.IsExpanded = !tv.IsExpanded;
      else if (tv.Tag is string path) {
         var file = File.ReadAllLines (path).ToList ();
         mLangDoc.Blocks.Clear ();
         var para = new Paragraph () { KeepTogether = true, TextAlignment = TextAlignment.Left };
         int count = 1;
         file.ToList ().ForEach ((line) => { para.Inlines.Add (new Run ($"{count++,4} │") { Foreground = Brushes.Red }); para.Inlines.Add (new Run ($"{line}\n")); });
         mLangDoc.Blocks.Add (para);
         mContentLoaded = true;
      }
   }
   #endregion
}