using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using static BookTool.Error;
namespace BookTool;

#region Class Patch -------------------------------------------------------------------------------
public static class Patch {
   #region Properties -----------------------------------------------
   public static Repository? Source { get; set; }
   public static Repository? Target { get; set; }
   public static string? CommitID { get; set; }
   public static List<string> Errors { get; } = [];
   public static List<string> PatchFile => sPatch ?? [];
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
      List<string> file1Content = [];
      ReadOnlySpan<char> file1 = null, file2 = null;
      int oldStartLine = -1, oldTotalLines = -1, aIndex, contextLine = 3;
      bool newFile = false, overwritten = false;
      for (int i = 0; i < sPatch.Count; i++) {
         string line = sPatch[i];
         if ((aIndex = line.IndexOf ("diff --git a/")) != -1) {
            (oldStartLine, oldTotalLines) = (-1, -1);
            var bIndex = line.IndexOf ("b/");
            file1 = line.AsSpan (aIndex + 13, bIndex - (aIndex + 13)).Trim ();
            file2 = line.AsSpan (bIndex + 2).Trim ();
            newFile = sPatch[++i].StartsWith("new file mode"); // It may be rename/copy, file will not exist in target rep in this case.
            if (!newFile && string.Compare (file1.ToString (), file2.ToString ()) == 0)
               file1Content = [.. File.ReadAllLines ($"{Target.Path}/{file1}")];
         } else if (line.StartsWith ("@@")) {
            // @@ -2,9 +2,8 @@ = @@ -a,b + c,d @@
            // we read 4 values and compare total lines changed. If it is a new file we consider c, d as startline and total lines
            // else if it is in index mode, we consider whichever has lesser number of lines changed.
            int a, b, c, d;
            // reading a,b:
            int j = line.IndexOf ('-') + 1, k = j + line[j..].IndexOf (','), l = k + line.AsSpan (k).IndexOf ('+');
            int.TryParse (line.AsSpan (j, k - j), out a);
            if (l <= k) int.TryParse (line.AsSpan (k), out b); // "@@ -1 +1,3 @@" old file was overwritten.
            else int.TryParse (line[(k + 1)..l], out b);
            // reading c, d
            j = line.IndexOf ('+') + 1; k = j + line[j..].IndexOf (','); l = k + line.AsSpan (k).IndexOf ('@');
            // @@ -0,0 +1 @@ : may or may not occur in new file mode, in which case we read the num of lines added and skip those.
            if (k <= j || newFile) {
               int.TryParse (line.AsSpan (j), out d); // it is a new file
               i += d + 1; // +1 for the "/ No newline at end of file" added in patch by git.
               continue;
            } else int.TryParse (line[(k + 1)..l], out d);
            int.TryParse (line.AsSpan (j, k - j), out c);
            (oldStartLine, oldTotalLines) = (c, d);
            contextLine = i;
            overwritten = b < d;
         } else if (oldStartLine != -1 && oldTotalLines != -1 && file1 != null && file2 != null) {
            int k, newStartLine = oldStartLine, newTotalLines = oldTotalLines;
            k = oldStartLine - 1;
            if (k > 1) sPatch[i - 1] = sPatch[i - 1][..(sPatch[i - 1].LastIndexOf ('@') + 1)] + ' ' + file1Content[k - 1]; // doesn't matter at all??????
            // replace context lines with translated text
            for (k = 0; k < oldTotalLines;) {
               switch (sPatch[i][0]) {
                  case '+': i++; /*if (overwritten) k++;*/ continue;
                  case '-': sPatch[i++] = '-' + file1Content[oldStartLine - 1 + k++]; continue;
                  case ' ': sPatch[i++] = ' ' + file1Content[oldStartLine - 1 + k++]; continue;
                  case '\\': i++; continue; // "/ No newline at end of file"
               }
               break;
            }
            (oldStartLine, oldTotalLines) = (-1, -1); i--;
         }
      }
      //File.WriteAllLines ($"{Target.Path}/change1.DE.patch", sPatch);
      return OK;
   }

   public static Error Apply () {
      if (Target == null) return SetTargetRepository;
      if (sPatch is null) return CannotApplyPatch;
      var results = RunHiddenCommandLineApp ("git.exe", $"switch {Target.Main}", out int nExit, workingdir: Target.Path);
      //results = RunHiddenCommandLineApp ("git.exe", $"restore .", out nExit, workingdir: Target.Path);
      //results = RunHiddenCommandLineApp ("git.exe", $"clean -f", out nExit, workingdir: Target.Path);
      File.WriteAllLines ($"{Target.Path}/change1.DE.patch", sPatch);
      results = RunHiddenCommandLineApp ("git.exe", $"apply change1.DE.patch", out nExit, workingdir: Target.Path);
      if (nExit != 0 || results.Count != 0) {
         Errors.AddRange (results);
         return CannotApplyPatch;
      }
      RunHiddenCommandLineApp ("git.exe", $"difftool --dir-diff", out _, workingdir: Target.Path);
      return OK;
   }
   #endregion

   #region Implmentation --------------------------------------------
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