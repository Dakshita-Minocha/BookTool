using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Management.Automation;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using static BookTool.MainWindow.Error;
using Path = System.IO.Path;

namespace BookTool;
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window {
   public MainWindow () {
      InitializeComponent ();
   }

   Error RunShellScript () {
      Reset ();
      if (mRep is null) return RepNotFound;
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
         if (results.Count is not 0) {
            powershell.AddScript ($"git branch -D {branchName}; git checkout -b {branchName} {mCommitID}");
         }
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

   static Error CompareFiles () {
      if (mOutFile is null) return OutFileNotFound;
      var list = mFilesOnMain.Concat (mFilesOnBranch).Select (a => a.Key).Distinct ();
      // Branch will always be BEHIND main
      // If the branch has extra files: files were deleted in later commits
      // If main has extra files: new files were added in subsequent commits
      List<string> changes = new ();
      int index = 0;
      foreach (var file in list) {
         if (changes.Count != 0) index = changes.Count;
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
         if (changes.Count != index) { AddFile (file, index); mTreeViewItems.Add (file); }
      }
      if (changes.Count != 0) { WriteToFile (mOutFile, changes); return OK; }
      return NoChangesMadeAfterCommit;

      // Helper methods -------------------------------------------
      void AddChange (string change, int line) => changes.Add ($"   {line}   {change}\n");
      void AddChangeOnly (string change) => changes.Add ($"   {change}\n");
      void AddFile (string fileName, int insertAt) {
         changes.Insert (insertAt, $"{fileName}\n");
         changes.Add ("EOF\n");
      }
   }

   static void WriteToFile (string outFile, List<string> changes) {
      File.WriteAllLines (outFile, changes);
   }


   static void GatherFiles (string rep, Dictionary<string, string[]> fileList) {
      AddFiles (rep, fileList);

      // HELPER
      static void AddFiles (string folder, Dictionary<string, string[]> fileList) {
         DirectoryInfo directoryInfo = new (folder);
         var gatheredFiles = directoryInfo.EnumerateFiles ("*.*", SearchOption.AllDirectories)
                                    .Where (f => f.Extension.Equals (".adoc", StringComparison.OrdinalIgnoreCase) ||
                                                f.Extension.Equals (".md", StringComparison.OrdinalIgnoreCase) ||
                                                f.Extension.Equals (".txt", StringComparison.OrdinalIgnoreCase))
                                    .Select (f => f.FullName)
                                    .ToArray ();
         foreach (var file in gatheredFiles) {
            if (fileList.TryGetValue (file, out var _))
               fileList[file] = File.ReadAllLines ($"{file}");
            else fileList.TryAdd (file, File.ReadAllLines (file));
         }
      }
   }

   #region WPF Events -----------------------------------------------
   void OnMouseWheel (object sender, MouseWheelEventArgs e)
      => mTreeScroll.ScrollToVerticalOffset (-e.Delta + mTreeScroll.VerticalOffset);

   void OnSelectionChanged (object sender, RoutedPropertyChangedEventArgs<object> e) {
      if (sender is not TreeView tv || tv.SelectedItem is not TreeViewItem selected) return;
      selected.IsExpanded = !selected.IsExpanded;
      if (selected.Tag is not string path || !File.Exists (path)) return;
      Reset ();
      //mFlowDocRight.Blocks.Clear ();
      //ScrollTo (0);
      //mContentLoaded = false;
      //AddContent (path);
   }

   void OnOpenClicked (object sender, RoutedEventArgs e) {
      Reset ();
      OpenFolderDialog fd = new () { Multiselect = false, DefaultDirectory = "C:" };
      fd.ShowDialog ();
      var err = SetRep ();

      // Helper Methods ---------------------------------------------
      Error SetRep () {
         string folderPath = fd.FolderName;
         mSelectedRep.Content = folderPath;
         mRep = folderPath.ToString ();
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
   static readonly char[] mSeparator = { '\\', '/' };

   void OnClickContentLoad (object sender, RoutedEventArgs e) {
      UpdateDoc (Clear);
      if (mOutFile is null) return;
      var f = File.Create (mOutFile);
      f?.Close ();
      Error error = Clear;
      if ((error = ValidateCommitID ()) != OK) { UpdateDoc (error); return; }
      if ((error = RunShellScript ()) != OK) { UpdateDoc (error); return; }
      error = CompareFiles ();
      UpdateDoc (error);
      mFileTree.ItemsSource = mTreeViewItems.Select (a => a.Split (mSeparator).LastOrDefault ());
   }
   static List <string> mTreeViewItems = [];

   Error ValidateCommitID () {
      if (mTBCommitID.Text is not string id) return InvalidCommitID;
      if (id.Length < 6 && id.Any (x => !char.IsAsciiLetterOrDigit (x))) return InvalidCommitID;
      mCommitID = id;
      return OK;
   }

   void Reset () { mFlowDocLeft.Blocks.Clear (); mFileTree.ItemsSource = null; mTreeViewItems.Clear (); }

   void UpdateDoc (Error err) {
      mFlowDocLeft.Blocks.Clear ();
      var para = new Paragraph ();
      if (mOutFile is null || mCommitID is null) return;
      switch (err) {
         case OK:
            var file = File.ReadAllLines (mOutFile).ToList ();
            file.ForEach (line => para.Inlines.Add (new Run ($"{line}\n"))); break;
         case Clear: break;
         default: para.Inlines.Add (new Run (err.ToString ())); break;
      }
      mFlowDocLeft.Blocks.Add (para);
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
      RepNotFound,
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
   #endregion
}