using System.Collections.ObjectModel;
using System.Formats.Asn1;
using System.IO;
using System.Management.Automation;
using static BookTool.Error;
namespace BookTool;
// Generate patch - done
// Copy english patch to target repository. - implement target repo (later)
// Read change from patch, attempt to form para.
// Apply patch



public static class Patch {
   public static Repository? Source { get; set; }
   public static Repository? Target { get; set; }
   public static string? CommitID {  get; set; }
   public static List<string> Errors { get; } = [];

   public static Error Generate () {
      if (Source == null) return SetSource;
      if (Target == null) return SetTargetRepository;
      Collection<PSObject> results;
      string branchName = $"changesUpto.{CommitID}";
      using (PowerShell powershell = PowerShell.Create ()) {
         var root = (Path.GetPathRoot (Source.Path) ?? "C:").Replace ("\\", "");
         powershell.AddScript ($"{root}");
         powershell.Invoke ();
         powershell.AddScript ($"cd {root}\\;cd {Source.Path}; git sw {Source.Main}");
         powershell.Invoke ();
         powershell.AddScript ("git lg");
         results = powershell.Invoke (); powershell.Streams.Error.Clear ();
         if (results.Count is 0) return NoCommitsFound;
         if (results.FirstOrDefault (a => a.ToString ().StartsWith ($"* {CommitID}") && a.ToString ().Contains ($"(HEAD -> master, {branchName})")) is not null)
            return NoChangesMadeAfterCommit; // (HEAD -> master, changesUpto.f7a2d7d)
         if (File.Exists ($"{Target.Path}/change1.DE.patch")) File.Delete ($"{Target.Path}/change1.DE.patch");
         powershell.AddScript ($"git diff {CommitID} > change1.patch");
         results = powershell.Invoke ();
         powershell.AddScript ($"copy change1.patch \"{Target.Path}/change1.DE.patch\"");
         powershell.Invoke ();
         //if (powershell.HadErrors) { Errors.AddRange (powershell.Streams.Error.Select (a => a.ErrorDetails.ToString ())); return ErrorGeneratingPatch; }
         powershell.AddScript ($"git checkout -b {branchName} {CommitID}");
         results = powershell.Invoke ();
         if (results.Count is not 0)
            powershell.AddScript ($"git branch -D {branchName}; git checkout -b {branchName} {CommitID}");
         powershell.AddScript ($"git checkout {branchName}");
         powershell.Invoke ();
      }
      return OK;
   }

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
      List<string> patchFile = [.. File.ReadAllLines ($"{Target.Path}/change1.DE.patch")];
      List<string> file1Content = [];
      ReadOnlySpan<char> file1 = null, file2 = null;
      int oldStartLine = -1, oldTotalLines = -1, aIndex, contextLine = 3;
      for (int i = 0; i < patchFile.Count; i++) {
         string line = patchFile[i];
         if ((aIndex = line.IndexOf ("diff --git a/")) != -1) {
            (oldStartLine, oldTotalLines) = (-1, -1);
            var bIndex = line.IndexOf ("b/");
            file1 = line.AsSpan (aIndex + 13, bIndex - (aIndex + 13));
            file1Content = [.. File.ReadAllLines ($"{Target.Path}/{file1}")];
            file2 = line.AsSpan (bIndex + 2);
            if (file1 != file2) { } // rename, copy
         } else if (line.StartsWith ("@@")) { // minimum 2 '@'
            int j = 1;
            while (!char.IsDigit (line[j++])) ;
            int idx = j - 1;
            while (line[j++] != ',') ;
            int.TryParse (line.AsSpan (idx, j - 1 - idx), out oldStartLine);
            idx = j;
            while (char.IsDigit (line[j++])) ;
            int.TryParse (line.AsSpan (idx, j - 1 - idx), out oldTotalLines);
            contextLine = i;
         } else if (oldStartLine != -1 && oldTotalLines != -1 && file1 != null && file2 != null) {
            int k, newStartLine = oldStartLine, newTotalLines = oldTotalLines;
            // insert para lines (before generated context)
            for (k = oldStartLine - 1; k >= 0 && file1Content[k].EndsWith ('\n'); k--) {
               patchFile.Insert (contextLine, ' ' + file1Content[k]);
               newStartLine = k + 1;
               newTotalLines++;
            }
            k = oldStartLine - 1;
            if (k > 1) patchFile[i - 1] = patchFile[i - 1][..(patchFile[i - 1].LastIndexOf ('@') + 1)] + ' ' + file1Content[k - 2];
            // replace context lines with translated text
            for (k = 0; k < oldTotalLines; k++) {
               switch (patchFile[i][0]) {
                  case '+': i++; k--; break;
                  case '-': patchFile[i++] = '-' + file1Content[oldStartLine - 1 + k]; break;
                  case ' ': patchFile[i++] = ' ' + file1Content[oldStartLine - 1 + k]; break;
               }
            }
            // insert lines to complete para (after context lines)
            for (k = oldStartLine + oldTotalLines; k < file1Content.Count && file1Content[k].EndsWith ('\n'); k++) patchFile.Add (file1Content[k]);

            // change line value in contextLine
            patchFile[contextLine] = patchFile[contextLine].Replace ($"{oldStartLine},{oldTotalLines}", $"{newStartLine},{newTotalLines}");
            (oldStartLine, oldTotalLines) = (-1, -1); i--;
         }
      }
      File.WriteAllLines ($"{Target.Path}/change1.DE.patch", patchFile);
      return OK;
   }

   public static Error Apply () {
      if (Target == null) return SetTargetRepository;
      if (!File.Exists ($"{Target.Path}/change1.DE.patch")) return CannotApplyPatch;
      using (PowerShell powershell = PowerShell.Create ()) {
         var root = (Path.GetPathRoot (Target.Path) ?? "C:").Replace ("\\", "");
         powershell.AddScript ($"{root}");
         powershell.AddScript ($"cd {root}\\;cd \"{Target.Path}\"; git sw main");
         powershell.Invoke ();
         powershell.Streams.Error.Clear ();
         powershell.AddScript ($"git apply change1.DE.patch");
         powershell.Invoke ();
         if (powershell.HadErrors && powershell.Streams.Error.Count > 1) { // Error [0] is 'already on main'
            Errors.AddRange (powershell.Streams.Error.Select (x => x.Exception.Message.ToString ()));
            return CannotApplyPatch;
         }
         powershell.AddScript ($"git di");
         powershell.Invoke ();
      }
      return OK;
   }
}

public record Repository (string Path, string Main);

#region Enum Error ---------------------------------------------------
public enum Error {
   OK = 0,
   SetSource,
   SetTargetRepository,
   SelectedFolderIsNotARepository,
   NoCommitsFound,
   InvalidCommitID,
   NoChangesMadeAfterCommit,
   ErrorGeneratingPatch,
   CannotApplyPatch,
   PatchApplied,
   Clear
}
#endregion