using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using static BookTool.Patch.Mode;
using static BookTool.Error;
namespace BookTool;

#region Class Sew ---------------------------------------------------------------------------------
public static class Sew {
   #region Properties -----------------------------------------------
   public static Repository? Source { get; set; }
   public static Repository? Target { get; set; }
   public static string? CommitID { get; set; }
   public static List<string> Errors { get; } = [];
   public static Patch? Patch => sPatch;
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
      if (res.Count == 0) return NoChangesMadeAfterCommit;
      if (Patch.ReadFromPatch (res) is not Patch patch) return ErrorGeneratingPatch;
      sPatch = patch;
      // For Debugging: File.WriteAllLines ($"{Target?.Path}/debug.file", res);
      AddContext ();
      return OK;
   }
   static Patch? sPatch;

   public static Error ProcessPatch () {
      if (Source == null) return SetSource;
      if (Target == null) return SetValidTargetRepository;
      if (sPatch == null) return ErrorGeneratingPatch;
      try {
         string[] fileContent;
         var changes = sPatch.Changes;
         foreach (var change in changes) {
            if (change.Mode is not Edit or New) continue;
            string path = Path.Join (Target.Path, change.File);
            if (!File.Exists (path)) continue;
            fileContent = File.ReadAllLines (path);
            if (change.StartLine > 1) change.AdditionalContext = fileContent[change.StartLine - 2];
            // If lines have been added, condition needs to be changed
            for (int i = 0, j = change.StartLine - 1; i < change.Content.Count; i++)
               if (change.Content[i][0] is not '+' and not '\\') change.Content[i] = change.Content[i][0] + $"{fileContent[j++]}";
         }
      } catch { return ErrorGeneratingPatch; }
      return OK;
   }

   public static Error SavePatchInTargetRep () {
      if (Target == null) return SetValidTargetRepository;
      if (sPatch is null) return CannotApplyPatch;
      File.WriteAllLines ($"{Target.Path}/{CommitID}.sew", sPatch.ConvertToSew ());
      return OK;
   }

   public static Error LoadPatchFileFromRep (string path) {
      if (Target == null) return SetValidTargetRepository;
      if (File.Exists (path))
         sPatch = path.EndsWith (".patch") ? Patch.ReadFromPatch (path) : Patch.ReadFromSew (path);
      if (sPatch is not null) CommitID = Path.GetFileNameWithoutExtension (path);
      return OK;
   }

   public static Error Apply () {
      if (Target == null) return CannotApplyPatch;
      if (sPatch == null) return CannotApplyPatch;
      RunHiddenCommandLineApp ("git.exe", $"switch {Target.Main}", out _, workingdir: Target.Path);
      var imageMap = RunHiddenCommandLineApp ("git.exe", $"lfs ls-files --long", out _, workingdir: Target.Path)
                        .Select (a => { var b = a.Split ('*'); return new KeyValuePair<string, string> ($"{b[1].Trim ()}", b[0].Trim ()); }).ToDictionary ();
      File.WriteAllLines ($"{Target.Path}/{CommitID}.patch", sPatch.ConvertToPatch (imageMap).Select (a => a.Replace ("\n\r", "\n")));
      var results = RunHiddenCommandLineApp ("git.exe", $"apply --ignore-space-change --whitespace=nowarn --allow-overlap {CommitID}.patch -v", out int nExit, workingdir: Target.Path);
      if (nExit != 0) {
         if (results.Count != 0) Errors.AddRange ([.. results.Where (a => a.StartsWith ("error: "))]);
         return CannotApplyPatch;
      }
      RunHiddenCommandLineApp ("git.exe", $"difftool --dir-diff", out _, workingdir: Target.Path);
      return OK;
   }
   #endregion

   #region Implmentation --------------------------------------------
   static Error AddContext () {
      if (Source == null) return SetSource;
      if (sPatch == null) return ErrorGeneratingPatch;
      var changes = sPatch.Changes.Where (a => a.Mode is Edit);
      foreach (var change in changes) {
         string path = Path.Join (Source.Path, change.File);
         if (!File.Exists (path)) { change.Mode = Delete; continue; }
         var file1Content = File.ReadAllLines (Path.Join (Source.Path, change.File));
         if (change.StartLine > 4) {
            change.StartLine -= 3; change.TotalLines += 3;
            change.Content.InsertRange (0, file1Content[(change.StartLine - 1)..(change.StartLine + 2)].Select (a => $" {a}"));
         }
         if (change.StartLine + change.TotalLines + 3 < file1Content.Length) {
            change.TotalLines += 3;
            change.Content.AddRange (file1Content[(change.StartLine + change.TotalLines - 4)..(change.StartLine + change.TotalLines - 1)].Select (a => $" {a}"));
         }
      }
      return OK;
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
/// <summary>A patch is the sum total of all the changes in the repository. It can be assembled from a .patch or a .sew file.</summary>
public record Patch () {
   #region Properties -----------------------------------------------
   public List<Change> Changes = [];
   #endregion

   #region Methods --------------------------------------------------
   public string[] ConvertToPatch (Dictionary<string, string> imageMap) {
      List<string> outFile = [];
      var groups = Changes.GroupBy (a => a.File);
      int count = groups.Count ();
      for (int i = 0; i < count; i++) {
         var element = groups.ElementAt (i);
         var firstChange = element.First ();
         var mode = firstChange.Mode;
         switch (mode) {
            case New:
               outFile.Add ($"diff --git a/dev/null b/{firstChange.File.Trim ()}");
               outFile.Add ($"new file mode 100644\nindex 0000000..0000000\n--- /dev/null\n+++ b/{firstChange.File.Trim ()}");
               AddChange (element);
               break;
            case Delete:
               outFile.Add ($"diff --git a/{firstChange.File.Trim ()} b/{firstChange.File.Trim ()}");
               outFile.Add ($"deleted file mode 100644\nindex 0000000..0000000 100644\n--- a/{firstChange.File.Trim ()}\n+++ /dev/null");
               if (firstChange.File.EndsWith (".png")) {
                  outFile.Add ($"@@ -1,3 +0,0 @@ \n-version https://git-lfs.github.com/spec/v1\n" +
                     $"-oid sha256:{imageMap[firstChange.File]}\n" +
                     $"-size {new FileInfo (Path.Combine (Sew.Target!.Path, firstChange.File)).Length}");
               }
               else AddChange (element);
               break;
            case Rename:
               outFile.Add ($"diff --git a/{firstChange.File.Trim ()} b/{firstChange.RenameTo!.Trim ()}");
               outFile.Add ($"similarity index 100%\nrename from {firstChange.File.Trim ()}\nrename to {firstChange.RenameTo.Trim ()}");
               break;
            case Edit:
               outFile.Add ($"diff --git a/{firstChange.File.Trim ()} b/{firstChange.File.Trim ()}");
               outFile.Add ($"index 0000000..0000000 100644\n--- a/{firstChange.File.Trim ()}\n+++ b/{firstChange.File.Trim ()}");
               AddChange (element);
               break;
         }
      }
      return [.. outFile];

      // Helper
      void AddChange (IGrouping<string, Change> element) {
         foreach (var change in element) {
            outFile.Add (change.ToString ());
            change.Content.ForEach (a => a.Replace ("\n\r", "\n"));
            outFile.AddRange (change.Content);
         }
      }
   }

   public string[] ConvertToSew () {
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
            case New:
               outFile.Add ($"Lines: {element.First ().TotalLines}");
               foreach (var change in element) {
                  outFile.Add (change.AdditionalContext);
                  outFile.AddRange (change.Content);
               }
               break;
            case Delete:
               break;
            case Rename:
               outFile.Add ($"To: {element.First ().RenameTo}");
               break;
            case Edit:
               foreach (var change in element) {
                  outFile.Add ($"Line: {change.StartLine}{(change.WasTotalChanged ? $",{change.LinesChanged} " : " ")}".PadRight (69, '-'));
                  outFile.Add (change.AdditionalContext);
                  outFile.AddRange (change.Content);
               }
               break;
         }
      }
      return [.. outFile];
   }

   public static Patch? ReadFromPatch (string path) => ReadFromPatch (File.ReadAllLines (path));

   public static Patch? ReadFromPatch (IEnumerable<string> patchFile) {
      var file = patchFile.ToList ();
      Patch patch = new ();
      ReadOnlySpan<char> file1 = "";
      Change? change = null;
      Mode mode = Edit;
      try {
         for (int i = 0; i < file.Count; i++) {
            string line = file[i];
            switch (line[0]) {
               case 'd':
                  int aIndex = line.IndexOf ("diff --git a/"), bIndex = line.IndexOf ("b/");
                  if (aIndex != -1) {
                     file1 = line.AsSpan (aIndex + 13, bIndex - (aIndex + 13));
                  }
                  if (file[i].StartsWith ("deleted file mode 100644")) mode = Delete;
                  else if (file[i + 1].StartsWith ("new file mode")) mode = New;
                  else if (file[i + 2].StartsWith ("rename from")) mode = Rename;
                  else mode = Edit;
                  if (mode is Delete) {
                     change = new Change (file1.ToString ().Trim ()) with { Mode = mode };
                     patch.Changes.Add (change);
                  }
                  if (mode is Rename) {
                     change = new Change (file1.ToString ().Trim ()) with { Mode = Rename, RenameTo = line[(bIndex + 2)..], Content = file[i..(i + 4)] };
                     patch.Changes.Add (change);
                     if (file[i + 4].StartsWith ("index")) {
                        // File has been renamed AND has changes in it. In this case, we rename it and then make changes in edit mode.
                        mode = Edit;
                        file1 = change.RenameTo;
                        i += 3;
                     }
                  }
                  i += 3;
                  break;
               case '@':
                  if (file1 == null) break;
                  if (mode is not Delete) {
                     change = new Change (file1.ToString ().Trim ()) with { Mode = mode };
                     patch.Changes.Add (change);
                  }
                  if (change is null) break;
                  var (sL, tL, sL2, tL2) = ParseLine (line);
                  if (sL2 == 0 && tL2 == 0) change.Mode = Delete;
                  else
                     // We always consider number of lines added (d).
                     (change.StartLine, change.TotalLines) = sL == -1 ? (1, tL) : // @@ -1 +1,3 @@ : file overwritten
                                                          sL2 == -1 || mode is New ? (1, tL2) : // @@ 0,0 +1 @@ : new file
                                                          (Math.Min (sL, sL2), tL > tL2 ? tL : tL2);  // @@ -4,4 +4,3 @@ : line deleted
                  if (mode is Edit && tL != tL2) {
                     change.WasTotalChanged = true;
                     change.LinesChanged = tL2 - tL;
                  }
                  change.AdditionalContext = line[(line.LastIndexOf ('@') + 1)..];
                  break;
               case '+': if (!line.StartsWith ("+++")) change?.Content.Add (line); break;
               case '-':
               case ' ':
               case '\\': change?.Content.Add (line); break; // "/ No newline at end of file" marks EOF
            }
         }
      } catch {
         return null;
      }
      return patch;
   }

   public static Patch? ReadFromSew (string path) => ReadFromSew (File.ReadAllLines (path));

   public static Patch? ReadFromSew (IEnumerable<string> patchFile) {
      var file = patchFile.ToArray ();
      Patch? patch = new ();
      for (int i = 0; i < file.Length;) {
         var fileName = file[i++].Split (':')[1].Trim ().Trim ('-');
         if (!Enum.TryParse (file[i++].Split (':')[1].Trim (), out Mode mode)) {
            patch = null; break;
         }
         var change = new Change (fileName.Trim ()) with { Mode = mode };
         if (mode is Delete) {
            change.StartLine = 1;
            change.Content.AddRange (File.ReadAllLines (Path.Combine (Sew.Target!.Path, fileName)).Select (a => $"-{a}"));
            patch?.Changes.Add (change);
            change.TotalLines = change.Content.Count;
            continue;
         }
         if (mode is Rename) {
            change.RenameTo = file[i++].Split (':')[1].Trim ();
            patch?.Changes.Add (change);
            continue;
         }
         while (i < file.Length && file[i].StartsWith ("Line")) {
            var split = file[i++].Split (':')[1].Trim ().Split (',');
            if (!int.TryParse (split[0].Trim ('-'), out int startLine)) {
               patch = null; break;
            }
            if (mode is New) { change.StartLine = 1; change.TotalLines = startLine; }
            else change.StartLine = startLine;
            if (split.Length == 2)
               if (!int.TryParse (split[1].TrimEnd ('-'), out int linesChanged)) {
                  patch = null; break;
               } else {
                  change.WasTotalChanged = true;
                  change.LinesChanged = linesChanged;
               }
            change.AdditionalContext = file[i++];
            int count = 0;
            while (i < file.Length && !file[i].StartsWith ("Line:") && !file[i].StartsWith ("File:")) {
               switch (file[i][0]) {
                  case ' ': count++; break;
                  case '+': count++; break;
                  case '-': case '\\': break;
                  default:
                     file[i] = file[i].Insert (0, " "); break;
               }
               change.Content.Add (file[i++]);
            }
            change.TotalLines = count;
            patch?.Changes.Add (change);
            change = new Change (fileName.Trim ()) { Mode = mode };
         }
      }
      return patch;
   }

   /// <summary>Parses diff string starting with @@</summary>
   // @@ -2,9 +2,8 @@ is equivalent to @@ -sL,tL +sL2,tL2 @@
   static (int StartLine, int TotalLines, int StartLine2, int TotalLines2) ParseLine (string line) {
      int sL, sL2, tL, tL2;
      // reading sL,tL:
      int j = line.IndexOf ('-') + 1, k = j + line[j..].IndexOf (','), l = k + line.AsSpan (k).IndexOf ('+');
      if (!int.TryParse (line[j..k], out sL)) (sL, tL) = (-1, int.Parse (line[j..l])); // @@ -1 +1,3 @@ : file overwritten | new file
      else int.TryParse (line[(k + 1)..l], out tL);
      // reading sL2, tL2
      j = line.IndexOf ('+') + 1; k = j + line[j..].IndexOf (','); l = k + line.AsSpan (k).IndexOf ('@');
      if (k <= j) (sL2, tL2) = (-1, int.Parse (line[j..l])); // @@ -0,0 +1 @@ | new file mode
      else (sL2, tL2) = (int.Parse (line[j..k]), int.Parse (line[(k + 1)..l]));
      if (sL == 0) sL++;
      if (sL2 == 0) sL2++;
      return (sL, tL, sL2, tL2);
   }
   #endregion

   #region Nested Types ---------------------------------------------
   public enum Mode {
      New, Delete, Rename, Edit
   }
   #endregion
}
#endregion

#region Record Change -----------------------------------------------------------------------------
public record Change (string File) {
   #region Properties -----------------------------------------------
   /// <summary>Edit, New, Delete, Rename</summary>
   public Patch.Mode Mode;

   /// <summary>Patch Content</summary>
   public List<string> Content = [];

   /// <summary>Line where context starts</summary>
   public int StartLine = -1;

   /// <summary>Total number of lines changed.</summary>
   public int TotalLines = -1;

   /// <summary>Content on StartLine - 1 in file. Used in .patch file</summary>
   public string AdditionalContext = "";

   /// <summary>Was the total number of lines changed?</summary>
   public bool WasTotalChanged = false;

   /// <summary>Number of lines Changed. +ve = lines added; -ve = lines deleted</summary>
   public int LinesChanged = 0;

   /// <summary>File Path after renaming. Used only in Rename Mode</summary>
   public string? RenameTo;
   #endregion

   #region Overrides ------------------------------------------------
   public override string ToString () => $"@@ -{(Mode is New ? "0,0" : $"{StartLine},{(WasTotalChanged ? TotalLines - (LinesChanged > 0 ? LinesChanged : 0) : TotalLines)}")} " +
                                         $"+{(Mode is Delete ? "0,0" : $"{StartLine},{TotalLines + (LinesChanged < 0 ? LinesChanged : 0)}")} @@ {AdditionalContext}";
   #endregion
}
#endregion

#region Enum Error --------------------------------------------------------------------------------
public enum Error {
   OK = 0,
   SetSource,
   SetValidTargetRepository,
   SelectedFolderIsNotARepository,
   NoChangesMadeAfterCommit,
   ErrorGeneratingPatch,
   RepositoryMismatch,
   CannotApplyPatch,
   Clear
}
#endregion