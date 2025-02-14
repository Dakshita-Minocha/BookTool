using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using static BookTool.Error;
namespace BookTool;

#region Class Patch -------------------------------------------------------------------------------
public static class Patch {
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
      if (Source == null) return SetSource;
      if (Target == null) return SetTargetRepository;
      if (sPatch == null) return ErrorGeneratingPatch;
      List<string>? file1Content = null;
      ReadOnlySpan<char> file1;
      int startLine = -1, totalLines = -1, aIndex, fileLine = 0;
      bool newFile = false, fileDeleted = false, rename;
      try {
         for (int i = 0; i < sPatch.Count; i++) {
            string line = sPatch[i];
            switch (line[0]) {
               case 'd':
                  (startLine, totalLines, file1Content) = (-1, -1, null);
                  aIndex = line.IndexOf ("diff --git a/");
                  fileDeleted = sPatch[i].StartsWith ("deleted file mode 100644");
                  newFile = sPatch[++i].StartsWith ("new file mode");
                  rename = sPatch[i + 1].StartsWith ("rename from");
                  if (rename || fileDeleted) { i++; break; }
                  if (newFile || aIndex == -1) break;
                  var bIndex = line.IndexOf ("b/");
                  file1 = line.AsSpan (aIndex + 13, bIndex - (aIndex + 13)).Trim ();
                  file1Content = [.. File.ReadAllLines (Path.Join (Target.Path, file1))];
                  break;
               case '@':
                  fileLine = 0;
                  var (sL, tL, sL2, tL2) = ParseLine (line);
                  if (fileDeleted) { i += tL + 1; break; }
                  if (tL > tL2) { i += tL; break; }
                  (startLine, totalLines) = (Math.Min (sL, sL2), tL2);
                  if (file1Content != null && startLine > 1)
                     sPatch[i] = $"{sPatch[i][..(sPatch[i].LastIndexOf ('@') + 1)]} {file1Content[startLine - 2]}";
                  break;
               case '+': break;
               case '-':
                  if (file1Content is null) break;
                  if (startLine != -1 && totalLines != -1 && fileLine < totalLines - 1) sPatch[i] = '-' + file1Content[startLine - 1 + fileLine++];
                  break;
               case ' ':
                  if (file1Content is null) break;
                  if (startLine != -1 && totalLines != -1 && fileLine < startLine - 1 + totalLines - 1) sPatch[i] = ' ' + file1Content[startLine - 1 + fileLine++]; break;
               case '\\': break;
            }
         }
         sPatch.ForEach (a => a.Replace ("\n\r", "\n"));
      } catch {
         return ErrorGeneratingPatch;
      }
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
      ReadOnlySpan<char> file1;
      int startLine = -1, totalLines = -1, aIndex;
      bool newFile = false, fileDeleted = false, rename = false;
      try {
         for (int i = 0; i < sPatch.Count; i++) {
            string line = sPatch[i];
            switch (line[0]) {
               case 'd':
                  aIndex = line.IndexOf ("diff --git a/");
                  fileDeleted = sPatch[i].StartsWith ("deleted file mode 100644");
                  newFile = sPatch[++i].StartsWith ("new file mode");
                  rename = sPatch[i + 1].StartsWith ("rename from");
                  if (file1Content != null && startLine != -1 && totalLines != -1 && (startLine + totalLines) <= file1Content.Count) {
                     sPatch.InsertRange (i - 1, file1Content[(startLine + totalLines - 4)..(startLine + totalLines - 1)].Select (a => $" {a}"));
                     i += 3;
                  }
                  if (rename || fileDeleted) { i++; break; }
                  if (newFile || aIndex == -1) break;
                  var bIndex = line.IndexOf ("b/");
                  file1 = line.AsSpan (aIndex + 13, bIndex - (aIndex + 13)).Trim ();
                  file1Content = [.. File.ReadAllLines (Path.Join (Source.Path, file1))];
                  (startLine, totalLines, file1Content) = (-1, -1, null);
                  break;
               case '@':
                  if (file1Content is null) break;
                  if (startLine != -1 && totalLines != -1 && (startLine + totalLines) <= file1Content.Count) {
                     sPatch.InsertRange (i - 1, file1Content[(startLine + totalLines - 4)..(startLine + totalLines - 1)].Select (a => $" {a}"));
                     i += 3;
                  }
                  var (sL, tL, sL2, tL2) = ParseLine (line);
                  if (sL == -1) { // @@ -1 +1,3 @@ : file overwritten
                     (startLine, totalLines) = (1, tL);
                     sPatch[i] = $"@@ -1,{totalLines} +{startLine},{totalLines} @@ ";
                     i = i + totalLines + 1;
                     continue;
                  }
                  if (fileDeleted) {
                     i += totalLines + 1;
                     break;
                  }
                  if (sL2 == -1) { // @@ 0,0 +1 @@ : new file
                     (startLine, totalLines) = (1, tL2);
                     sPatch[i] = $"@@ -0,0 +{startLine},{totalLines} @@";
                     i = i + totalLines + 1; // +1 for "/ No newline at end of file"
                     continue;
                  }
                  if (newFile) {
                     i = i + totalLines + 1;
                     continue;
                  }
                  if (tL > tL2) { i += tL; break; } // @@ -4,4 +4,3 @@ : line deleted

                  // We always consider number of lines added (d).
                  (startLine, totalLines) = (Math.Min (sL, sL2), tL2);
                  
                  // adding 3 lines for context after current line.
                  if (startLine > 4) {
                     startLine -= 3; totalLines += 3; tL2 += 3; tL += 3;
                     sPatch.InsertRange (i + 1, file1Content[(startLine - 1)..(startLine + 2)].Select (a => $" {a}"));
                  }
                  // 3 lines for context after patch
                  if (startLine + totalLines + 3 < file1Content.Count) totalLines += 3;
                  sPatch[i] = $"@@ -{startLine},{(tL != tL2 ? tL : totalLines)} +{startLine},{totalLines} @@ {(startLine > 1 ? file1Content[startLine - 2] : "")}";
                  break;
               case '+':
               case '-':
               case ' ':
               case '\\': break; // "/ No newline at end of file" marks EOF
            }
         }
         if (startLine != -1 && totalLines != -1 && (startLine + totalLines) <= file1Content?.Count)
            sPatch.InsertRange (sPatch.Count, file1Content[(startLine + totalLines - 4)..(startLine + totalLines - 1)].Select (a => $" {a}"));
      } catch {
         return ErrorGeneratingPatch;
      }
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
public record Repository (string Path, string Main);
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