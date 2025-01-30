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

   void Reset () { UpdateDoc (mLangDoc, Clear); UpdateDoc (mENDoc, Clear); mTreeViewItems.Clear (); mFileTree.Items.Clear (); mBtnApply.IsEnabled = false; mChanges.Clear (); }

   void UpdateDoc (FlowDocument doc, Error err) {
      doc.Blocks.Clear ();
      var para = new Paragraph () { KeepTogether = true, TextAlignment = TextAlignment.Left };
      switch (err) {
         case OK:
            mChanges.ForEach (line => para.Inlines.Add (new Run ($"{line}\n") {
               Background = line[0] is '+' ? Brushes.GreenYellow :
                            line[0] is '-' ? Brushes.Red :
                            Brushes.Transparent
            })); mContentLoaded = true; mMaxRows = mChanges.Count + 1; break;
         case Clear: mContentLoaded = false; break;
         default: para.Inlines.Add (new Run ($"Error: {err}\n")); Errors.ForEach (x => para.Inlines.Add (new Run ($"{x}\n"))); Errors.Clear (); mContentLoaded = true; break;
      }
      doc.Blocks.Add (para);
   }
   #endregion

   #region WPF Events -----------------------------------------------
   void OnMouseWheel (object sender, MouseWheelEventArgs e)
      => mTreeScroll.ScrollToVerticalOffset (-e.Delta + mTreeScroll.VerticalOffset);

   void OnSelectionChanged (object sender, RoutedPropertyChangedEventArgs<object> e) {
      if (sender is not TreeView tv || tv.SelectedItem is not string selected) return;
      CommitID = selected.Split ("  ")[0];
      Reset ();
      Error err;
      if ((err = Generate ()) != OK) { UpdateDoc (mENDoc, err); return; }
      err = ProcessPatch ();
      if (err == OK) mChanges = PatchFile;
      UpdateDoc (mENDoc, err);
      mBtnApply.IsEnabled = true;
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
      Reset ();
      OpenFolderDialog fd = new () { Multiselect = false, DefaultDirectory = "C:" };
      if (fd.ShowDialog () == true) UpdateDoc (mENDoc, SetRep (btn.Name == "mOpenEN"));

      // Helper Methods ---------------------------------------------
      Error SetRep (bool source) {
         string folderPath = fd.FolderName;
         if (Directory.GetDirectories (folderPath, ".git", SearchOption.TopDirectoryOnly).Length == 0) return SelectedFolderIsNotARepository;
         var results = RunHiddenCommandLineApp ("git.exe", $"branch", out _, workingdir: folderPath);
         Repository rep = new (folderPath, results.Select (a => a.ToString ()).First (a => a.Contains ("main") || a.Contains ("master")).Trim ('*'));
         if (source) {
            Source = rep;
            mSelectedRep.Content = folderPath;
            RunHiddenCommandLineApp ("git.exe", $"switch {Source.Main}", out int nExit, workingdir: folderPath);
            results = RunHiddenCommandLineApp ("git.exe", "log -n 30 --format=\"%h  %s\"", out nExit, workingdir: folderPath);
            mCommitTree.ItemsSource = results.ToList ();
         } else {
            Target = rep;
            mTargetRep.Content = folderPath;
         }
         return OK;
      }
   }
   static List<FileInfo> mTreeViewItems = [];
   static List<string> mChanges = [];

   void OnClickApply (object sender, RoutedEventArgs e) {
      var err = Apply ();
      UpdateDoc (mLangDoc, err);
      if (err == OK) PopulateTreeView ();
      else mFileTree.ItemsSource = null;
   }

   /// <summary>Populates Treeview with underlying directories and files.</summary>
   void PopulateTreeView () {
      mFileTree.Items.Clear ();
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