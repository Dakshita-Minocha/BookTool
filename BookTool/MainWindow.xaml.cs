using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static BookTool.MainWindow.Error;
using Path = System.IO.Path;
namespace BookTool;

public partial class MainWindow : Window {
   public MainWindow () {
      InitializeComponent ();
      mGTLCommand = new RelayCommand (GoToLine, ContentLoaded);
      InputBindings.Add (new KeyBinding (mGTLCommand, mGTLGesture));
      mFileTree.ItemsSource = mTreeViewItems;
      mRunHeight = (int)new FormattedText ("9999", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface (mFlowDoc.FontFamily.ToString ()), mFlowDoc.FontSize, Brushes.Black, null, 1).Height;
      Loaded += delegate {
         mScroller = (ScrollViewer)((Decorator)VisualTreeHelper.GetChild (VisualTreeHelper.GetChild (mDocScroller, 0), 0)).Child;
         //mBtnExport.InputBindings.Add (new CommandBinding (mContentLoaded, (s, e) => mBtnExport.IsEnabled = !IsEnabled));
      };
   }
   readonly RelayCommand mGTLCommand;
   readonly KeyGesture mGTLGesture = new (Key.G, ModifierKeys.Control);

   #region Implmentation --------------------------------------------
   void AddContext (string path) {
      mFlowDoc.Blocks.Clear ();
      var para = new Paragraph ();
      int line = 0;
      if (!mFilesOnMain.TryGetValue (path, out var mainContent)) para.Inlines.Add (new Run ("File Deleted"));
      else foreach (var item in mainContent) {
            para.Inlines.Add (new Run ($"{++line,4} │ {item}\n"));
            (mMaxRows, mContentLoaded) = (mainContent.Length, true);
      }
      mFlowDoc.Blocks.Add (para);
   }

   /// <summary>Returns true if FlowDocument content has been loaded.</summary>
   // Using a get-only method instead of property so it can be passed
   // to the RelayCommand Constructor as a canExecute condition
   static bool ContentLoaded () => mContentLoaded;
   static bool mContentLoaded;

   Error RunShellScript () {
      Reset ();
      if (mRep is null) return SelectedFolderIsNotARepository;
      if (mCommitID is null) return InvalidCommitID;
      Collection<PSObject> results;
      string branchName = $"changesUpto.{mCommitID}";
      mFilesOnBranch.Clear ();
      mFilesOnMain.Clear ();
      using (PowerShell powershell = PowerShell.Create ()) {
         var root = (Path.GetPathRoot (mRep) ?? "C:").Replace ("\\", "");
         powershell.AddScript ($"{root}");
         results = powershell.Invoke ();
         powershell.AddScript ($"cd {root}\\;cd {mRep}; git sw {mMain}");
         results = powershell.Invoke ();
         GatherFiles (mRep, mFilesOnMain);
         powershell.AddScript ("git lg");
         results = powershell.Invoke ();
         if (results.Count is 0) return NoCommitsFound;
         if (results.FirstOrDefault (a => a.ToString ().StartsWith ($"* {mCommitID}") && a.ToString ().Contains ($"(HEAD -> master, {branchName})")) is not null)
            return NoChangesMadeAfterCommit; //  (HEAD -> master, changesUpto.f7a2d7d)
         powershell.AddScript ($"git checkout -b {branchName} {mCommitID}");
         results = powershell.Invoke ();
         if (results.Count is not 0)
            powershell.AddScript ($"git branch -D {branchName}; git checkout -b {branchName} {mCommitID}");
         powershell.AddScript ($"git checkout {branchName}");
         results = powershell.Invoke ();
         // diff highlights changes made AFTER selected commit ID
         // Left side: main (most recent version of repository)
         // Right side: Changes upto (including) selected commitID
         powershell.AddScript ($"git di {mMain}");
         results = powershell.Invoke ();
         GatherFiles (mRep, mFilesOnBranch);
      }
      return OK;
   }

   /// <summary>Scrolls to line number</summary>
   public void ScrollTo (int pos) => mScroller?.ScrollToVerticalOffset (pos * mRunHeight);
   ScrollViewer? mScroller;
   readonly int mRunHeight;

   static Error CompareFiles () {
      if (mOutFile is null) return OutFileNotFound;
      var list = mFilesOnMain.Concat (mFilesOnBranch).Select (a => a.Key).Distinct ();
      // Branch will always be BEHIND main
      // If the branch has extra files: files were deleted in later commits
      // If main has extra files: new files were added in subsequent commits
      mChanges.Clear ();
      int index = 0;
      foreach (var file in list) {
         if (mChanges.Count != 0) index = mChanges.Count;
         bool branchExists = mFilesOnBranch.TryGetValue (file, out var branchContent);
         bool mainExists = mFilesOnMain.TryGetValue (file, out var mainContent);
         if (!branchExists && mainExists)
            for (int i = 0; i < mainContent!.Length; i++) AddChange ($"+ {mainContent[i]}", i + 1);
         else if (!mainExists && branchExists)
            AddChangeOnly ($"File Deleted");
         else {
            // If file contains more lines on main: lines were added
            // If file containes more lines on branch: lines were removed
            // Everything else was "replaced" and we display mainContent there.
            int mainLen = mainContent!.Length, branchLen = branchContent!.Length;
            for (int i = 0; i < mainLen; i++) {
               if (i >= branchLen) AddChange ($"+ {mainContent[i]}", i + 1);
               else if (branchContent[i] != mainContent[i]) AddChange ($"-+ {mainContent[i]}", i + 1);
            }
            for (int i = mainLen; i < branchLen; i++) AddChange ($"- {branchContent[i]}", i + 1);
         }
         if (mChanges.Count != index) {
            AddFile (file, index);
            mTreeViewItems.Add (new FileInfo (file));
         }
      }
      return mChanges.Count != 0 ? OK : NoChangesMadeAfterCommit;

      // Helper methods -------------------------------------------
      void AddChange (string change, int line) => mChanges.Add ($"   {line,4} │   {change}\n");
      void AddChangeOnly (string change) => mChanges.Add ($"   {change}\n");
      void AddFile (string fileName, int insertAt) {
         mChanges.Insert (insertAt, $"{fileName}\n");
         mChanges.Add ("EOF\n");
      }
   }

   static void WriteToFile (string outFile, List<string> changes) => File.WriteAllLines (outFile, changes);

   static void GatherFiles (string rep, Dictionary<string, string[]> fileList) {
      DirectoryInfo directoryInfo = new (rep);
      foreach (var file in directoryInfo.EnumerateFiles ("*.*", SearchOption.AllDirectories)
                                 .Where (f => f.Extension.Equals (".adoc", StringComparison.OrdinalIgnoreCase) ||
                                              f.Extension.Equals (".md", StringComparison.OrdinalIgnoreCase) ||
                                              f.Extension.Equals (".txt", StringComparison.OrdinalIgnoreCase))
                                 .Select (f => f.FullName)) {
         if (fileList.TryGetValue (file, out var _))
            fileList[file] = File.ReadAllLines ($"{file}");
         else fileList.TryAdd (file, File.ReadAllLines (file));
      }
   }
   #endregion

   #region WPF Events -----------------------------------------------
   void OnMouseWheel (object sender, MouseWheelEventArgs e)
      => mTreeScroll.ScrollToVerticalOffset (-e.Delta + mTreeScroll.VerticalOffset);

   void OnSelectionChanged (object sender, RoutedPropertyChangedEventArgs<object> e) {
      if (sender is not TreeView tv || tv.SelectedItem is not TreeViewItem selected) return;
      selected.IsExpanded = !selected.IsExpanded;
      if (selected.Tag is not FileInfo file) return;
      AddContext (file.FullName);
   }

   /// <summary>Navigates to user entered line number.</summary>
   void GoToLine () {
      mGoToLine = new GoToLine (mMaxRows) { Owner = this };
      if (mGoToLine.ShowDialog () is true) ScrollTo (mGoToLine.LineNumber);
   }
   int mMaxRows;
   GoToLine? mGoToLine;

   void OnOpenClicked (object sender, RoutedEventArgs e) {
      Reset ();
      OpenFolderDialog fd = new () { Multiselect = false, DefaultDirectory = "C:" };
      fd.ShowDialog ();
      var err = SetRep ();
      if (err != OK) UpdateDoc (err);

      // Helper Methods ---------------------------------------------
      Error SetRep () {
         string folderPath = fd.FolderName;
         mSelectedRep.Content = folderPath;
         if (Directory.GetDirectories (folderPath, ".git", SearchOption.TopDirectoryOnly).Length == 0) return SelectedFolderIsNotARepository;
         mRep = folderPath.ToString ();
         var folder = $"{mRep}html";
         if (!Directory.Exists (folder)) Directory.CreateDirectory (folder);
         mOutFile = $"{mRep}html/Changes.txt";

         using (PowerShell powershell = PowerShell.Create ()) {
            powershell.AddScript ($"cd {(Path.GetPathRoot (mRep) ?? "C:\\").Replace ("\\", "")}");
            var results = powershell.Invoke ();
            powershell.AddScript ($"cd {mRep}");
            powershell.AddScript ("git branch");
            results = powershell.Invoke ();
            mMain = results.Select (a => a.ToString ()).First (a => a.Contains ("main") || a.Contains ("master")).Trim ('*');
         }
         return OK;
      }
   }

   void OnClickContentLoad (object sender, RoutedEventArgs e) {
      UpdateDoc (Clear);
      if (mOutFile is null) return;
      var f = File.Create (mOutFile);
      f?.Close ();
      Error error;
      if ((error = ValidateCommitID ()) != OK) { UpdateDoc (error); return; }
      if ((error = RunShellScript ()) != OK) { UpdateDoc (error); return; }
      error = CompareFiles ();
      UpdateDoc (error);
      mFileTree.ItemsSource = mTreeViewItems.Select (a => new TreeViewItem () { Header = a.Name, Tag = a });
      mBtnExport.IsEnabled = true;
   }
   static List<FileInfo> mTreeViewItems = [];

   Error ValidateCommitID () {
      if (mTBCommitID.Text is not string id) return InvalidCommitID;
      if (id.Length < 6 && id.Any (x => !char.IsAsciiLetterOrDigit (x))) return InvalidCommitID;
      mCommitID = id;
      return OK;
   }

   void Reset () { mFlowDoc.Blocks.Clear (); mFileTree.ItemsSource = null; mTreeViewItems.Clear (); mBtnExport.IsEnabled = false; }

   void UpdateDoc (Error err) {
      mFlowDoc.Blocks.Clear ();
      var para = new Paragraph ();
      switch (err) {
         case OK:
            mChanges.ForEach (line => para.Inlines.Add (new Run ($"{line}\n"))); break;
         case Clear: break;
         default: para.Inlines.Add (new Run (err.ToString ())); break;
      }
      mFlowDoc.Blocks.Add (para);
   }

   void OnClickExport (object sender, RoutedEventArgs e) {
      Error error = OK;
      if (mOutFile is null) error = OutFileNotFound;
      else if (mChanges.Count < 0) error = NoCommitsFound;
      UpdateDoc (error);
      if (mOutFile is not null) WriteToFile (mOutFile, mChanges);
   }

   void OnTreeViewExpanded (object sender, RoutedEventArgs e) {
      //if (((TreeViewItem)sender).Header is not string folder) return;
      //var mod = mOutput.Modules.Where (x => x.Name == folder).FirstOrDefault ();
      //Current = mod is null ? -1 : mOutput.Modules.IndexOf (mod);
   }
   #endregion

   #region Nested Types ---------------------------------------------
   public enum Error {
      OK = 0,
      SelectedFolderIsNotARepository,
      OutFileNotFound,
      NoCommitsFound,
      InvalidCommitID,
      TextFilesNotFound,
      NoChangesMadeAfterCommit,
      Clear
   }
   #endregion

   #region Private Data ---------------------------------------------
   static string? mRep, mCommitID, mOutFile, mMain;
   static Dictionary<string, string[]> mFilesOnMain = new (), mFilesOnBranch = new ();
   static List<string> mChanges = [];
   #endregion
}