using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Controls;
using System.Xml.Linq;
using static BookTool.Error;
using static BookTool.Patch.Mode;
namespace BookTool;

#region Class Sew ---------------------------------------------------------------------------------
public static class Sew {
   #region Properties -----------------------------------------------
   public static Repository? Source { get; set; }
   public static Repository? Target { get; set; }
   public static string? CommitID { get; set; }
   public static List<string> Errors { get; } = [];
   public static List<string>? PatchFile => sPatch;
   #endregion

   #region Methods --------------------------------------------------
   public static Error Generate () {
      if (Source == null) return SetSource;
      RunHiddenCommandLineApp ("git.exe", $"switch {Source.Main}", out _, workingdir: Source.Path);
      var res = RunHiddenCommandLineApp ("git.exe", $"diff {CommitID}", out int nExit, workingdir: Source.Path);
      if (nExit != 0) {
         Errors.AddRange (res);
         return ErrorGeneratingPatch;
      }
      sPatch = res;
      AddContext ();
      if (res.Count == 0) return NoChangesMadeAfterCommit;
      return OK;
   }
   static List<string>? sPatch;

   /// <summary>Used to gather data from patch and apply to laguage repositories.</summary>
   /// https://git-scm.com/docs/diff-generate-patch
   /// Patch format:
   /// diff --git a/CreateBendMC.adoc b/CreateBendMC.adoc
   /// Extended header
   /// a/file mode
   /// b/file mode
   /// @@ -line, column + line, column @@ previous line content
   /// context (3 lines)
   /// -change
   /// +change
   /// context (3 lines)
   public static Error ProcessPatch () {
      //if (Source == null) return SetSource;
      //if (Target == null) return SetTargetRepository;
      //if (sPatch == null) return ErrorGeneratingPatch;
      //List<string>? file1Content = null;
      //ReadOnlySpan<char> file1;
      //int startLine = -1, totalLines = -1, aIndex, fileIdx = 0;
      //bool newFile = false, fileDeleted = false, rename;
      //try {
      //   for (int i = 0; i < sPatch.Count; i++) {
      //      string line = sPatch[i];
      //      switch (line[0]) {
      //         case 'd':
      //            (startLine, totalLines, file1Content) = (-1, -1, null);
      //            aIndex = line.IndexOf ("diff --git a/");
      //            fileDeleted = sPatch[i].StartsWith ("deleted file mode 100644");
      //            newFile = sPatch[++i].StartsWith ("new file mode");
      //            rename = sPatch[i + 1].StartsWith ("rename from");
      //            if (rename || fileDeleted) { i++; break; }
      //            if (newFile || aIndex == -1) break;
      //            var bIndex = line.IndexOf ("b/");
      //            file1 = line.AsSpan (aIndex + 13, bIndex - (aIndex + 13)).Trim ();
      //            file1Content = [.. File.ReadAllLines (Path.Join (Target.Path, file1))];
      //            break;
      //         case '@':
      //            var (sL, tL, sL2, tL2) = ParseLine (line);
      //            if (fileDeleted) { i += tL + 1; break; }
      //            if (tL > tL2) { i += tL; break; }
      //            (startLine, totalLines) = (Math.Min (sL, sL2), tL2);
      //            fileIdx = startLine - 1;
      //            if (file1Content != null && startLine > 1)
      //               sPatch[i] = $"{sPatch[i][..(sPatch[i].LastIndexOf ('@') + 1)]} {file1Content[fileIdx - 1]}";
      //            break;
      //         case '+': break;
      //         case '-':
      //            if (file1Content is null) break;
      //            if (startLine != -1 && totalLines != -1 && fileIdx < startLine + totalLines) sPatch[i] = '-' + file1Content[fileIdx++];
      //            break;
      //         case ' ':
      //            if (file1Content is null) break;
      //            if (startLine != -1 && totalLines != -1 && fileIdx < startLine + totalLines) sPatch[i] = ' ' + file1Content[fileIdx++]; break;
      //         case '\\': break;
      //      }
      //   }
      //   sPatch.ForEach (a => a.Replace ("\n\r", "\n"));
      //} catch {
      //   return ErrorGeneratingPatch;
      //}
      return OK;
   }

   public static Error SavePatchInTargetRep () {
      if (Target == null) return SetTargetRepository;
      if (sPatch is null) return CannotApplyPatch;
      sPatch.ForEach (a => a.Replace ("\n", "\n\r"));
      File.WriteAllLines ($"{Target.Path}/change1.DE.patch", sPatch);
      return OK;
   }

   public static Error LoadPatchFileFromRep () {
      if (Target == null) return SetTargetRepository;
      if (File.Exists ($"{Target.Path}/*.patch")) sPatch = [.. File.ReadAllLines ($"{Target.Path}/*.patch")];
      return OK;
   }

   public static Error Apply () {
      if (Target == null) return SetTargetRepository;
      RunHiddenCommandLineApp ("git.exe", $"switch {Target.Main}", out _, workingdir: Target.Path);
      var results = RunHiddenCommandLineApp ("git.exe", $"apply --ignore-space-change --whitespace=nowarn --allow-overlap change1.DE.patch -v", out int nExit, workingdir: Target.Path);
      if (nExit != 0) {
         if (results.Count != 0) Errors.AddRange ([.. results.Where (a => a.StartsWith ("error: "))]);
         return CannotApplyPatch;
      }
      RunHiddenCommandLineApp ("git.exe", $"difftool --dir-diff", out _, workingdir: Target.Path);
      sPatch ??= [.. File.ReadAllLines ($"{Target.Path}/change1.DE.patch")];
      return OK;
   }
   #endregion

   #region Implmentation --------------------------------------------
   static Error AddContext () {
      if (Source == null) return SetSource;
      if (sPatch == null) return ErrorGeneratingPatch;
      List<string>? file1Content = new ();
      ReadOnlySpan<char> file1 = "";
      int aIndex;
      Patch patch = new ();
      Change? change = null;
      Patch.Mode mode = NewFile;
      try {
         for (int i = 0; i < sPatch.Count; i++) {
            string line = sPatch[i];
            switch (line[0]) {
               case 'd':
                  aIndex = line.IndexOf ("diff --git a/");
                  if (sPatch[i].StartsWith ("deleted file mode 100644")) mode = Delete;
                  else if (sPatch[i + 1].StartsWith ("new file mode")) mode = NewFile;
                  else if (sPatch[i + 2].StartsWith ("rename from")) mode = Rename;
                  else mode = Edit;
                  // 3 lines for context after patch
                  if (mode is Edit && change != null && change.TotalLines + 3 < file1Content.Count) {
                     change.TotalLines += 3;
                     change.Content.AddRange (file1Content[(change.StartLine + change.TotalLines - 4)..(change.StartLine + change.TotalLines - 1)].Select (a => $" {a}"));
                  }
                  var bIndex = line.IndexOf ("b/");
                  file1 = line.AsSpan (aIndex + 13, bIndex - (aIndex + 13)).Trim ();
                  if (mode is Delete) {
                     change = new (file1.ToString (), mode);
                     change.Content = sPatch[i..(i + 4)];
                     patch.Changes.Add (change); i += 3;
                     break;
                  }
                  if (mode is Rename) {
                     change = new (file1.ToString (), mode);
                     change.RenameTo = line[(bIndex + 2)..]; change.Content = sPatch[i..(i + 4)];
                     patch.Changes.Add (change); i += 3;
                     break;
                  }

                  file1Content = [.. File.ReadAllLines (Path.Join (Source.Path, file1))];
                  i += 3;
                  break;
               case '@':
                  if (file1 == null || file1Content is null) break;
                  // 3 lines for context after patch
                  if (mode is Edit && change != null && change.TotalLines + 3 < file1Content.Count) {
                     change.TotalLines += 3;
                     change.Content.AddRange (file1Content[(change.StartLine + change.TotalLines - 4)..(change.StartLine + change.TotalLines - 1)].Select (a => $" {a}"));
                  }
                  change = new (file1.ToString (), mode);
                  patch.Changes.Add (change);
                  var (sL, tL, sL2, tL2) = ParseLine (line);
                  if (sL == -1) { // @@ -1 +1,3 @@ : file overwritten
                     (change.StartLine, change.TotalLines) = (1, tL);
                     change.Content.AddRange (sPatch[(i + 1)..(i + change.TotalLines + 1)]);
                     i += change.TotalLines;
                     continue;
                  }
                  if (mode == Delete) {
                     //i += totalLines + 1;
                     break;
                  }
                  if (sL2 == -1) { // @@ 0,0 +1 @@ : new file
                     (change.StartLine, change.TotalLines) = (1, tL2);
                     i += change.TotalLines; // +1 for "/ No newline at end of file"
                     continue;
                  }
                  if (mode == NewFile) {
                     change.Content.AddRange (sPatch[(i + 1)..(i + change.TotalLines + 1)]);
                     i += change.TotalLines;
                     continue;
                  }
                  if (tL > tL2) {
                     change.Content.AddRange (sPatch[(i + 1)..(i + tL + 1)]);
                     i += tL;
                     break;
                  } // @@ -4,4 +4,3 @@ : line deleted

                  // We always consider number of lines added (d).
                  (change.StartLine, change.TotalLines) = (Math.Min (sL, sL2), tL2);

                  // adding 3 lines for context after current line.
                  if (change.StartLine > 4) {
                     change.StartLine -= 3; change.TotalLines += 3; tL2 += 3; tL += 3;
                     change.Content.InsertRange (0, file1Content[(change.StartLine - 1)..(change.StartLine + 2)].Select (a => $" {a}"));
                  }
                  //if (change.StartLine + change.TotalLines + 3 < file1Content.Count) change.TotalLines += 3;
                  if (change.StartLine > 1) change.AdditionalContext = file1Content[change.StartLine - 2];
                  break;
               case '-':
               case '+':
               case ' ':
               case '\\': change?.Content.Add (line); break; // "/ No newline at end of file" marks EOF
            }
         }
         if (mode is Edit && change != null && change.TotalLines > change.Content.Count)
            change.Content.AddRange (file1Content[(change.StartLine + change.TotalLines - 4)..(change.StartLine + change.TotalLines - 1)].Select (a => $" {a}"));
      } catch {
         return ErrorGeneratingPatch;
      }
      patch.ExportReadableVersion ($"{Target.Path}/change1.DE.outFile");
      return OK;
   }

   /// <summary>Parses diff string starting with @@</summary>
   // @@ -2,9 +2,8 @@ is equivalent to @@ -sL,tL +sL2,tL2 @@
   static (int StartLine, int TotalLines, int StartLine2, int TotalLines2) ParseLine (string line) {
      int tL, tL2;
      // reading sL,tL:
      int j = line.IndexOf ('-') + 1, k = j + line[j..].IndexOf (','), l = k + line.AsSpan (k).IndexOf ('+');
      if (!int.TryParse (line.AsSpan (j, k - j), out int sL)) sL = -1;
      if (l <= k) { // @@ -1 +1,3 @@ : file overwritten | new file
         int.TryParse (line.AsSpan (k - j), out tL);
      } else int.TryParse (line[(k + 1)..l], out tL);
      // reading sL2, tL2
      j = line.IndexOf ('+') + 1; k = j + line[j..].IndexOf (','); l = k + line.AsSpan (k).IndexOf ('@');
      if (k <= j) {
         int.TryParse (line.AsSpan (j, k - j), out tL2); // @@ -0,0 +1 @@ | new file mode
      } else int.TryParse (line[(k + 1)..l], out tL2);
      if (!int.TryParse (line.AsSpan (j, k - j), out int sL2)) sL2 = -1;
      return (sL, tL, sL2, tL2);
   }

   /// <summary>Executes a command-line program and returns the output it produces as a list of strings</summary>
   /// This routine captures stdout and runs a command line appliaction. Any data that application
   /// writes to stdout will be gathered and returend as a list of strings. The app is never shown
   /// and runs in a hidden window; if the app ever stops for any input the calling process will
   /// wait forever. Be careful to use suitable command line arguments to prevent any prompting. 
   /// For example, when running SVN.EXE, use the command line argument --non-interactive. 
   /// <param name="exe">The name of the application to execute</param>
   /// <param name="args">The command line arguments for the application</param>
   /// <param name="nExit">This gets set to the exit code from the application you are running</param>
   /// <param name="encoding">The text encoding to use when reading from standard output</param>
   /// <param name="workingdir">The working directory to use (if null, uses the Current directory)</param>
   /// <returns>A list of strings constituting the output generated by that application</returns>
   public static List<string> RunHiddenCommandLineApp (string exe, string args, out int nExit, Encoding? encoding = null, string? workingdir = null) {
      List<string?> output = [];
      ProcessStartInfo startinfo = new () {
         CreateNoWindow = true, UseShellExecute = false, Arguments = args,
         WorkingDirectory = workingdir ?? Directory.GetCurrentDirectory (), FileName = exe,
         RedirectStandardError = true, RedirectStandardOutput = true,
         StandardOutputEncoding = encoding
      };
      if (Process.Start (startinfo) is not Process proc) throw new IOException ($"Unable to execute '{exe}'");
      // The following is the standard protocol for reading output from a command-line app. 
      // Make the call to BeginOutputReadLine(), and then do ReadToEnd() and THEN WaitForExit().
      // This doesn't look very kosher, but this is the only way it works correctly. 
      proc.OutputDataReceived += (_, e) => output.AddNonNull (e.Data);
      proc.ErrorDataReceived += (_, e) => output.AddNonNull (e.Data);
      proc.BeginOutputReadLine (); proc.BeginErrorReadLine ();
      proc.WaitForExit (); nExit = proc.ExitCode;
      proc.Close ();
      return [.. output];
   }

   /// <summary>Add an element if it is non-null</summary>
   static void AddNonNull<T> (this IList<T> list, T value) {
      if (value != null) list.Add (value);
   }

   /// <summary>Returns an absolute filename, given a filename relative to Flux.dll.</summary>
   /// This does not check if the file or path you specify here exists. It just computes the
   /// filename and returns it. 
   static string GetLocalFile (string file) {
      string? location = Assembly.GetEntryAssembly ()?.Location ?? GetLocation (Assembly.GetExecutingAssembly () ?? Assembly.GetCallingAssembly ());
      return Path.GetFullPath (Path.Combine (Path.GetDirectoryName (location) ?? "", file));
   }

   // Helpers ..............................
   static string? GetLocation (Assembly asm) {
      if (asm == null) return null;
      return new Uri (asm.Location).LocalPath;
   }
   #endregion
}
#endregion

#region Record Repository -------------------------------------------------------------------------
public record Repository (string Path, string Main) {
   public Patch? Patch { get; set; }
}
#endregion

#region Record Patch ------------------------------------------------------------------------------
public record Patch () {
   #region Properties -----------------------------------------------
   public List<Change> Changes = [];
   #endregion

   #region Methods --------------------------------------------------
   public void WriteToPatch (string path) {

   }
//diff --git a/3DImport.adoc b/Import3d.adoc
//similarity index 100%
//rename from 3DImport.adoc
//rename to Import3d.adoc
   public void ExportReadableVersion (string path) {
      List<string> outFile = [];
      var groups = Changes.GroupBy (a => a.File);
      int count = groups.Count ();
      for (int i = 0; i < count; i++) {
         var element = groups.ElementAt (i);
         var file = element.Key;
         var mode = element.First ().Mode;
         outFile.Add ($"File: {groups.ElementAt (i).Key} ".PadRight (99, '-'));
         outFile.Add ($"Mode: {mode}");
         switch (mode) {
            case NewFile:
               outFile.Add ($"Lines: {element.First ().TotalLines}");
               foreach (var change in element) outFile.AddRange (change.Content);
               break;
            case Delete:
               break;
            case Rename:
               outFile.Add ($"To: {element.First ().RenameTo}");
               break;
            case Edit:
               foreach (var change in element) {
                  outFile.Add ($"Line: {change.StartLine} ".PadRight (69, '-'));
                  outFile.AddRange (change.Content);
               }
               break;
         }
      }
      File.WriteAllLines (path, outFile);
   }

   public void ReadFromPatch (string path) => ReadFromPatch (File.ReadAllLines (path));
   public void ReadFromPatch (IEnumerable<string> patchFile) { }
   #endregion

   #region Nested Types ---------------------------------------------
   public enum Mode {
      NewFile, Delete, Rename, Edit
   }
   #endregion
}
#endregion

#region Record Change -----------------------------------------------------------------------------
public record Change (string File, Patch.Mode Mode) {
   /// <summary>Patch Content</summary>
   public List<string> Content = [];

   /// <summary>File Path after renaming. To be used only in Index/NewFile Mode</summary>
   public int StartLine = -1;
   public int TotalLines = -1;
   public string AdditionalContext = "";

   /// <summary>File Path after renaming. To be used only in Rename Mode</summary>
   public string? RenameTo;

   public override string ToString () => $"@@ -{StartLine}, {TotalLines} +{StartLine}, {TotalLines} @@ {AdditionalContext}";
}
#endregion

#region Enum Error --------------------------------------------------------------------------------
public enum Error {
   OK = 0,
   SetSource,
   SetTargetRepository,
   SelectedFolderIsNotARepository,
   NoChangesMadeAfterCommit,
   ErrorGeneratingPatch,
   CannotApplyPatch,
   Clear
}
#endregion