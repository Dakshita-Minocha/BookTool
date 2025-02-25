Sew
README

Sew helps to organise all changes made to a repository from a commit into a single file, edit changes and apply it to a corresponding repository.

Pre-requisites:
   - Install Dotnet and Git
   - Pull recent changes before running tool.

Sew Workflow:
   -> Generate .sew file for Target repository.
   -> Make changes (translate) if necessary.
   -> Import .sew file and apply changes to Target repository.

   To Generate .sew File:
      - Choose Source and Target repositories from the file explorer.
      - Choose the LAST MERGED/TRANSLATED commit from recent commits; The generated diffs will be shown in the flow documents.
         - The left diff corresponds to the source repository and the right diff to the Target.
         - If target is not chosen, diff generated will be of the changes made in the source repository.
      - Click on 'Export Patch' to save CommitID.sew file in target repository.

   To keep in mind while Editing the .Sew File:
   - File: relative path to file
     Mode: Edit | NewFile | Delete | Rename
     Line(s): StartLine, Number of lines changed.
   - The changes are marked by a '+' or '-' sign to indicate if a line was added or removed.
   - A single File can have multiple "Line" sub-sections, but only one mode.
   - Edits should ONLY be made in the lines starting with '+'.

   To Apply changes to the Target repository:
   - If .sew file already exists, click on 'Import Patch' button at the bottom of the screen to import it.
   - Click on Apply patch. The right document will contain a detailed description if any error is encountered.
   - If a patch is applicable, it is applied, and a diff is shown in winmerge/any difftool, if it is configured.
     Otherwise changes can also be seen in the tool’s right window by choosing files from the treeview on the left.

NOTE: Currently, only .png is supported for images.

Shortcuts:
   Press [Ctrl+G] to go to a specific line in document after content has loaded.
   Press [Ctrl+F] to find occurrences of words.