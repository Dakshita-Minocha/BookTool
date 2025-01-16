using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Management.Automation;
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
      mFileTree.ItemsSource = mTreeViewItems;
      mRunHeight = (int)new FormattedText ("9999", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface (mFlowDoc.FontFamily.ToString ()), mFlowDoc.FontSize, System.Windows.Media.Brushes.Black, null, 1).Height;
      Loaded += delegate {
         mScroller = (ScrollViewer)((Decorator)VisualTreeHelper.GetChild (VisualTreeHelper.GetChild (mDocScroller, 0), 0)).Child;
         //mBtnExport.InputBindings.Add (new CommandBinding (mContentLoaded, (s, e) => mBtnExport.IsEnabled = !IsEnabled));
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

   Error ValidateCommitID () {
      if (mTBCommitID.Text is not string id || id.Length < 6 || id.Any (x => !char.IsAsciiLetterOrDigit (x) || char.IsWhiteSpace (x))) return InvalidCommitID;
      CommitID = id;
      return OK;
   }

   void Reset () { UpdateDoc (Clear); mFileTree.ItemsSource = null; mTreeViewItems.Clear (); mBtnApply.IsEnabled = false; mChanges.Clear (); }

   void UpdateDoc (Error err) {
      mFlowDoc.Blocks.Clear ();
      var para = new Paragraph ();
      switch (err) {
         case OK:
            mChanges.ForEach (line => para.Inlines.Add (new Run ($"{line}\n") {
               Background = line[0] is '+' ? System.Windows.Media.Brushes.GreenYellow :
                            line[0] is '-' ? System.Windows.Media.Brushes.Red :
                            System.Windows.Media.Brushes.Transparent
            })); mContentLoaded = true; mMaxRows = mChanges.Count + 1; break;
         case Clear: mContentLoaded = false; break;
         default: para.Inlines.Add (new Run ($"Error: {err}\n")); Errors.ForEach (x => para.Inlines.Add (new Run ($"{x}\n"))); Errors.Clear (); mContentLoaded = true; break;
      }
      mFlowDoc.Blocks.Add (para);
   }
   #endregion

   #region WPF Events -----------------------------------------------
   void OnMouseWheel (object sender, MouseWheelEventArgs e)
      => mTreeScroll.ScrollToVerticalOffset (-e.Delta + mTreeScroll.VerticalOffset);

   void OnSelectionChanged (object sender, RoutedPropertyChangedEventArgs<object> e) {
      //if (sender is not TreeView tv || tv.SelectedItem is not TreeViewItem selected) return;
      //selected.IsExpanded = !selected.IsExpanded;
      //if (selected.Tag is not FileInfo file) return;
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
      if (fd.ShowDialog () == true) UpdateDoc (SetRep (btn.Name == "mOpenEN"));

      // Helper Methods ---------------------------------------------
      Error SetRep (bool source) {
         string folderPath = fd.FolderName;
         if (Directory.GetDirectories (folderPath, ".git", SearchOption.TopDirectoryOnly).Length == 0) return SelectedFolderIsNotARepository;
         using (PowerShell powershell = PowerShell.Create ()) {
            powershell.AddScript ($"cd {(Path.GetPathRoot (folderPath) ?? "C:\\").Replace ("\\", "")}");
            var results = powershell.Invoke ();
            powershell.AddScript ($"cd \"{folderPath}\"");
            powershell.AddScript ("git branch");
            results = powershell.Invoke ();
            Repository rep = new (folderPath, results.Select (a => a.ToString ()).First (a => a.Contains ("main") || a.Contains ("master")).Trim ('*'));
            if (source) {
               Source = rep;
               mSelectedRep.Content = folderPath;
            } else {
               Target = rep;
               mTargetRep.Content = folderPath;
            }
         }
         return OK;
      }
   }

   void OnClickContentLoad (object sender, RoutedEventArgs e) {
      UpdateDoc (Clear);
      Error err;
      if ((err = ValidateCommitID ()) != OK) { UpdateDoc (err); return; }
      if ((err = Generate ()) != OK) { UpdateDoc (err); return; }
      err = ProcessPatch (); mChanges = [.. File.ReadAllLines ($"{Target?.Path}/Change1.DE.patch")]; UpdateDoc (err);
      //mFileTree.ItemsSource = mTreeViewItems.Select (a => new TreeViewItem () { Header = a.Name, Tag = a });
      mBtnApply.IsEnabled = true;
   }
   static List<FileInfo> mTreeViewItems = [];
   static List<string> mChanges = [];

   void OnClickApply (object sender, RoutedEventArgs e) {
      Reset ();
      if (Apply () == OK) mChanges.Add ("Patch Applied.");
      UpdateDoc (OK);
      using (PowerShell powershell = PowerShell.Create ()) {
         var root = (Path.GetPathRoot (Target?.Path) ?? "C:").Replace ("\\", "");
         powershell.AddScript ($"{root}");
         powershell.AddScript ($"cd {root}\\;cd \"{Target.Path}\"; git sw main");
         powershell.Invoke ();
         powershell.AddScript ($"git di");
         powershell.Invoke ();
      }
   }

   void OnTBMouseDown (object sender, MouseButtonEventArgs e) {
      if (sender is not TextBox tb) return;
      if (tb.Text == "Last Translated Commit ID") tb.Text = "";
   }
   #endregion
}