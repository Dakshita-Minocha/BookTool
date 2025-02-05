BookTool
README

Booktool helps to organise all changes made to a repository from a commit into a single file.

Pre-requisites:
   - Install Dotnet and Git
   - Pull recent changes before running tool.

BookTool Workflow:
   - Choose Source and Target repositories from the file explorer.
   - Choose from recent commits the last translated commit; The generated diff will be shown in the left flow document.
      - If target is not chosen, diff generated will be of the changes made _since_ the chosen commit in the source repository.
   - Click on 'Export Patch' to save patch in target repository.
   - If patch already exists, click on 'Import Patch' button at the bottom of the screen to import it.
   - Click on Apply patch. The right document will contain a detailed description if any error is encountered.
	In most cases, re-loading the patch (going to a different commit and coming back to the current one) fixes it.
   - If a patch is applicable, it is applied, and a diff is shown in winmerge/any difftool, if it is configured.
	Otherwise the changes can also be seen in the tool’s right window by choosing files from the treeview on the left.

Shortcuts:
   Press [Ctrl+G] to go to a specific line in document after content has loaded.
   Press [Ctrl+F] to find occurrences of words.