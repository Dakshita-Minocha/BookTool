using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static BookTool.Error;
using static BookTool.Sew;
using Path = System.IO.Path;
namespace BookTool;

public partial class MainWindow : Window {
   public MainWindow () {
      InitializeComponent ();
      Title = "SEW";
      mGTLCommand = new RelayCommand (GoToLine, ContentLoaded);
      InputBindings.Add (new KeyBinding (mGTLCommand, mGTLGesture));
      mRunHeight = (int)new FormattedText ("9999", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface (mENDoc.FontFamily.ToString ()), mENDoc.FontSize, Brushes.Black, null, 1).Height;
      Loaded += delegate {
         mScroller = (ScrollViewer)((Decorator)VisualTreeHelper.GetChild (VisualTreeHelper.GetChild (mLangDocScroll, 0), 0)).Child;
         if (!Directory.Exists ($"{AppContext.BaseDirectory}/.Sew")) Directory.CreateDirectory ($"{AppContext.BaseDirectory}/.Sew");
         if (File.Exists ($"{AppContext.BaseDirectory}/.Sew/memory.txt")) {
            var pStatic = File.ReadAllLines ($"{AppContext.BaseDirectory}/.Sew/memory.txt");
            if (!string.IsNullOrEmpty (pStatic[0])) SetRep (true, pStatic[0]);
            if (!string.IsNullOrEmpty (pStatic[1])) SetRep (false, pStatic[1]);
         }
      };

      Closed += delegate {
         File.WriteAllLines ($"{AppContext.BaseDirectory}/.Sew/memory.txt", [Source?.Path??"", Target?.Path??""]);
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
      CurrentPatch = Sew.Patch;
      UpdateDoc (mENDoc, err);
      if (Target is null) return;
      UpdateDoc (mLangDoc, ProcessPatch ());
   }

   Patch? CurrentPatch {
      get { mPatchFile = Sew.Patch?.ConvertToSew (); return Sew.Patch; }
      set {
         mPatchFile = value?.ConvertToSew ();
         Sew.Patch = value;
      }
   }
   string[]? mPatchFile;

   void Reset () {
      UpdateDoc (mLangDoc, Clear); UpdateDoc (mENDoc, Clear);
      CurrentPatch = null;
      CommitID = null;
   }

   void UpdateDoc (FlowDocument doc, Error err) {
      doc.Blocks.Clear ();
      var para = new Paragraph () { KeepTogether = true, TextAlignment = TextAlignment.Left };
      mHyperLink.Content = "";
      switch (err) {
         case OK:
            if (CurrentPatch == null || mPatchFile == null) break;
            foreach (var line in mPatchFile)
               para.Inlines.Add (new Run ($"{line}\n") {
                  Background = line.FirstOrDefault () is '+' ? Brushes.GreenYellow :
                               line.FirstOrDefault () is '-' ? Brushes.Salmon :
                               Brushes.Transparent
               });
            mContentLoaded = true; mMaxRows = mPatchFile.Length + 1; break;
         case Clear: mContentLoaded = false; break;
         case CouldNotApplyAllChanges:
         default:
            para.Inlines.Add (new Run ($"Error: {err}\n") { Background = Brushes.Yellow, FontSize = 18 });
            Errors.ForEach (x => para.Inlines.Add (new Run ($"{x}\n")));
            Errors.Clear ();
            if (err == CouldNotApplyAllChanges) {
               mHyperLink.Content = $"file://{Target!.Path}/Failed.Sew";
               para.Inlines.Add (new Run ("\nLikely the content in the .sew file does not match the files in the repository.\nYou may click on the hyperlink below to see the failed patch and make the necessary changes."));
            }
            mContentLoaded = true;
            break;
      }
      doc.Blocks.Add (para);
   }
   #endregion

   #region WPF Events -----------------------------------------------
   void OnMouseWheel (object sender, MouseWheelEventArgs e)
      => mLeftTreeScroll.ScrollToVerticalOffset (-e.Delta + mLeftTreeScroll.VerticalOffset);

   void OnSelectionChanged (object sender, RoutedPropertyChangedEventArgs<object> e) {
      if (sender is not TreeView tv || tv.SelectedItem is not string selected) return;
      Reset ();
      CommitID = selected.Split ("  ")[0];
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
      Error err;
      if ((err = SetRep (setSource, fd.FolderName)) != OK) UpdateDoc (mLangDoc, err);
      else if (setSource) UpdateDoc (mENDoc, OK);
      else UpdateDoc (mLangDoc, OK);
   }

   Error SetRep (bool source, string folderPath) {
      if (Directory.GetDirectories (folderPath, ".git", SearchOption.TopDirectoryOnly).Length == 0) return SelectedFolderIsNotARepository;
      var results = RunHiddenCommandLineApp ("git.exe", $"branch", out _, workingdir: folderPath);
      Repository rep = new (folderPath, results.Select (a => a.ToString ()).First (a => a.Contains ("main") || a.Contains ("master")).Trim ('*'));
      UpdateDoc (mENDoc, Clear);
      UpdateDoc (mLangDoc, Clear);
      if (source) {
         Reset ();
         Source = rep;
         if (rep.Path == Target?.Path) return SetValidTargetRepository;
         mLabelSourceRep.Content = folderPath;
         mSourceCommitTree.ItemsSource = null;
         mSourceCommitTree.ItemsSource = GetRecentCommits (Source);
         if (CurrentPatch is not null) UpdateDoc (mENDoc, ProcessPatch ());
      } else {
         Reset ();
         Target = rep;
         if (rep.Path == Source?.Path) return SetValidTargetRepository;
         mLabelTargetRep.Content = folderPath;
         mTargetCommitTree.ItemsSource = null; mTargetCommitTree.Items.Clear ();
         mTargetCommitTree.ItemsSource = GetRecentCommits (Target);
         if (CurrentPatch is not null) UpdateDoc (mLangDoc, ProcessPatch ());
      }
      return OK;

      // Helper
      List<string> GetRecentCommits (Repository rep) {
         RunHiddenCommandLineApp ("git.exe", $"switch {rep.Main}", out _, workingdir: rep.Path);
         return RunHiddenCommandLineApp ("git.exe", "log -n 30 --format=\"%h  %s\"", out _, workingdir: rep.Path);
      }
   }

   void OnClickExport (object sender, RoutedEventArgs e) {
      Error err;
      if ((err = SavePatchInTargetRep ()) == OK)
         if (MessageBox.Show ("Would you like to open it?", ".Sew File Exported", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
            if (Target != null)
               RunHiddenCommandLineApp ("\"C:\\Program Files\\Notepad++\\notepad++.exe\"", $"\"{OutFile}\"", out _, workingdir: Target.Path);
         }
      else UpdateDoc (mLangDoc, err);
   }

   void OnClickImport (object sender, RoutedEventArgs e) {
      OpenFileDialog fd = new () { Multiselect = false, DefaultDirectory = Target?.Path ?? "C:", Filter = "Sew files (*.patch)|*.sew", CheckFileExists = true };
      if (fd.ShowDialog () != true) return;
      SetRep (false, Path.GetDirectoryName (fd.FileName)!);
      LoadPatchFileFromRep (fd.FileName);
      UpdateDoc (mLangDoc, OK);
   }

   void OnClickApply (object sender, RoutedEventArgs e) {
      var err = Apply ();
      UpdateDoc (mLangDoc, err);
      if (err == OK) PopulateTreeView ();
      else mTargetCommitTree.ItemsSource = null;
   }

   void OnClickReload (object sender, RoutedEventArgs e) {
      if (Source!= null) SetRep (true, Source.Path);
      if (Target != null) SetRep (false, Target.Path);
   }

   void OnClickRestore (object sender, RoutedEventArgs e) {
      RunHiddenCommandLineApp ("git.exe", $"restore .", out _, workingdir: Target?.Path);
      RunHiddenCommandLineApp ("git.exe", $"clean -f", out _, workingdir: Target?.Path);
      Reset ();
   }


   void OnHyperLinkClicked (object sender, RoutedEventArgs e) {
      if (Target != null)
         RunHiddenCommandLineApp ("\"C:\\Program Files\\Notepad++\\notepad++.exe\"", $"{Target.Path}/Failed.sew", out _, workingdir: Target.Path);
   }

   /// <summary>Populates Treeview with underlying directories and files.</summary>
   void PopulateTreeView () {
      mTargetCommitTree.ItemsSource = null;
      var info = new DirectoryInfo (Target!.Path);
      var dir = info.GetDirectories ().Where (a => !a.Name.StartsWith ('.'));
      foreach (var folder in dir) {
         var item = MakeTVI (folder.Name);
         if (Path.GetDirectoryName (folder.Name) is string path && !string.IsNullOrWhiteSpace (path)) AddSubItems (path, item);
         if (item.HasItems) mTargetCommitTree.Items.Add (item);
      }
      var item2 = MakeTVI (info.Name, expanded: true);
      AddSubItems (Target.Path, item2);
      if (item2.HasItems) mTargetCommitTree.Items.Add (item2);
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